using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Entitlements;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Entitlements;

/// <summary>
/// Computes <see cref="DoctorEntitlement"/> rows (M9 task 8). The pure calculators (exam-fee models,
/// System A, System B) and the toggle resolver do the arithmetic; this service supplies their inputs
/// from the database — the batch config, its (non-void) invoices and line cost snapshots, and the
/// <c>sale_value</c> (the settled price where the batch is settled, else the billed line unit_price —
/// M29 removed the contract-overridden tier; SCHEMA invariant #8).
///
/// <para>Idempotent per source: one entitlement per batch / per visit. A re-run refreshes the figures
/// on a still-<c>pending</c> row and is a no-op once the row is approved/paid (settled figures are
/// frozen). Always writes the row in <c>pending</c> status — releasing it is the settlement workflow's
/// job, gated by <see cref="ISettlementLockGuard"/>.</para>
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IExamFeeCalculatorFactory _examFees;
    private readonly IEntitlementToggleResolver _toggle;
    private readonly ISystemBDirectFeeCalculator _systemB;

    public EntitlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IExamFeeCalculatorFactory examFees,
        IEntitlementToggleResolver toggle,
        ISystemBDirectFeeCalculator systemB)
    {
        _db = db;
        _currentUser = currentUser;
        _examFees = examFees;
        _toggle = toggle;
        _systemB = systemB;
    }

    public async Task<DoctorEntitlement> ComputeForBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var breakdown = await ExplainForBatchAsync(batchId, cancellationToken);

        return await UpsertAsync(
            e => e.BatchId == batchId,
            breakdown.DoctorId,
            batchId: batchId,
            visitId: null,
            breakdown.System,
            breakdown.DoctorShare,
            cancellationToken);
    }

    public async Task<BatchEntitlementBreakdown> ExplainForBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
                    ?? throw new NotFoundException("batch", batchId);

        var (effectiveInvoices, productItems) = await LoadEffectiveInvoicesAsync(
            i => i.BatchId == batchId, cancellationToken);

        // M24 — a settled batch (تصفية) supersedes the billed/contract prices: the negotiated
        // per-product price wins, the revenue basis becomes the settled one (pre-discount), and the
        // batch-level discount enters the System-A basis below (invariant #8, client decisions).
        var settlement = await _db.BatchSettlements.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BatchId == batchId, cancellationToken);
        var settledPrices = settlement is null
            ? new Dictionary<Guid, decimal>()
            : await _db.BatchSettlementLines.AsNoTracking()
                .Where(l => l.SettlementId == settlement.Id)
                .ToDictionaryAsync(l => l.ProductId, l => l.SettledUnitPrice, cancellationToken);

        var revenue = effectiveInvoices.Values.Sum(i => i.Total) + (settlement?.RepricingDelta ?? 0m);

        // sale_value (M24 + M29): the settlement-line price where the batch is settled and the product
        // has one, else the line's snapshotted unit_price. M29 removed per-contract medication pricing,
        // so there is no longer a contract tier in between — the billed line price (itself catalog) is
        // the fallback. cost is the line's cost_price snapshot.
        var drugProfit = 0m;
        var drugCost = 0m;
        foreach (var item in productItems)
        {
            var saleValue = settledPrices.TryGetValue(item.ProductId!.Value, out var settledPrice)
                ? settledPrice
                : item.UnitPrice;

            drugProfit += (saleValue - item.CostPrice) * item.Quantity;
            drugCost += item.CostPrice * item.Quantity;
        }

        // M28 — the supervision fee IS the doctor's entitlement (both systems). The percent-of-invoice
        // model computes on the settled (pre-discount) revenue, mirroring SettleAsync's snapshot.
        var supervisionFee = _examFees.For(batch.SupervisionFeeModel)
            .Calculate(new ExamFeeBasis(batch.SupervisionFeeValue, batch.AnimalCount, revenue));

        var enabled = _toggle.IsEnabled(batch.EntitlementEnabled, await GlobalToggleAsync(cancellationToken));

        // The system is needed even when disabled (System B still charges the farmer the fee), so it must
        // be set whenever the batch carries any entitlement intent. Default to drug_profit only when both
        // the toggle is off AND no system was configured (no fee changes hands either way).
        var system = batch.EntitlementSystem
                     ?? (enabled
                         ? throw new ConflictException("entitlement_system_unset",
                             $"Batch '{batchId}' has the entitlement system enabled but no entitlement_system set.")
                         : EntitlementSystem.DrugProfit);

        var discountAmount = settlement?.DiscountAmount ?? 0m;
        var split = EntitlementSplitCalculator.Resolve(system, enabled, drugProfit, supervisionFee, discountAmount);

        return new BatchEntitlementBreakdown(
            batchId,
            batch.ResponsibleDoctorId,
            batch.CustomerId,
            batch.EndDate,
            system,
            enabled,
            revenue,
            drugCost,
            drugProfit,
            supervisionFee,
            split.DoctorShare,
            split.ClinicShare,
            FeeAddedToSettlement: split.FeeAddedToSettlement,
            FeeRetainedByClinic: split.FeeRetainedByClinic,
            SettlementDiscount: discountAmount);
    }

    public async Task<DoctorEntitlement?> ComputeForVisitAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                    ?? throw new NotFoundException("visit", visitId);

        // Visit-sourced entitlement is System B: the standalone exam-fee invoice(s) credited in full.
        var (examInvoices, _) = await LoadEffectiveInvoicesAsync(
            i => i.VisitId == visitId && i.InvoiceType == InvoiceType.ExamFee, cancellationToken);
        var fee = examInvoices.Values.Sum(i => i.Total);

        if (fee <= 0m)
        {
            return null; // no exam-fee basis ⇒ nothing to credit
        }

        var enabled = _toggle.IsEnabled(perBatchOverride: null, await GlobalToggleAsync(cancellationToken));
        var computedAmount = enabled ? _systemB.Calculate(fee).ComputedAmount : 0m;

        return await UpsertAsync(
            e => e.VisitId == visitId,
            visit.DoctorId,
            batchId: null,
            visitId: visitId,
            EntitlementSystem.DirectFee,
            computedAmount,
            cancellationToken);
    }

    /// <summary>Shared void-aware loader (M24 extraction) — see <see cref="EffectiveInvoices"/>.</summary>
    private Task<(Dictionary<Guid, Invoice> Invoices, List<InvoiceItem> ProductItems)> LoadEffectiveInvoicesAsync(
        System.Linq.Expressions.Expression<Func<Invoice, bool>> predicate, CancellationToken cancellationToken)
        => EffectiveInvoices.LoadAsync(_db, predicate, cancellationToken);

    private async Task<bool> GlobalToggleAsync(CancellationToken cancellationToken)
    {
        var envId = _currentUser.EnvironmentId
            ?? throw new ForbiddenException("unauthenticated", "Authentication required.");

        var global = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.EnvironmentId == envId)
            .Select(s => (bool?)s.EntitlementEnabledGlobal)
            .FirstOrDefaultAsync(cancellationToken);

        return global ?? true; // default-on if settings are missing, matching the seed default
    }

    private async Task<DoctorEntitlement> UpsertAsync(
        System.Linq.Expressions.Expression<Func<DoctorEntitlement, bool>> match,
        Guid doctorId,
        Guid? batchId,
        Guid? visitId,
        string system,
        decimal computedAmount,
        CancellationToken cancellationToken)
    {
        var existing = await _db.DoctorEntitlements.FirstOrDefaultAsync(match, cancellationToken);

        if (existing is not null)
        {
            // Frozen once settled — never recompute an approved/paid entitlement.
            if (existing.Status != EntitlementStatus.Pending)
            {
                return existing;
            }

            existing.DoctorId = doctorId;
            existing.CalculationSystem = system;
            existing.ComputedAmount = computedAmount;
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var entitlement = new DoctorEntitlement
        {
            Id = Guid.Empty,
            DoctorId = doctorId,
            BatchId = batchId,
            VisitId = visitId,
            CalculationSystem = system,
            ComputedAmount = computedAmount,
            Status = EntitlementStatus.Pending,
        };

        _db.DoctorEntitlements.Add(entitlement);
        await _db.SaveChangesAsync(cancellationToken);
        return entitlement;
    }
}
