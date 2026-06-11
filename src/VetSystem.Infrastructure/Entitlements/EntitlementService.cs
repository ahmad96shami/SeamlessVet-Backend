using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Entitlements;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Entitlements;

/// <summary>
/// Computes <see cref="DoctorEntitlement"/> rows (M9 task 8; M30 — batch-only). The pure calculators
/// (exam-fee models, the M28 split) and the toggle resolver do the arithmetic; this service supplies
/// their inputs from the database — the batch config, its (non-void) invoices and line cost snapshots,
/// and the <c>sale_value</c> (the settled price where the batch is settled, else the billed line
/// unit_price — M29 removed the contract-overridden tier; SCHEMA invariant #8).
///
/// <para>Idempotent per batch: one entitlement per batch, refreshed on a re-run. The settlement
/// workflow calls this once when the batch is settled; the row is an immutable accrual audit and the
/// same amount is credited to the doctor-partner ledger (M30).</para>
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IExamFeeCalculatorFactory _examFees;
    private readonly IEntitlementToggleResolver _toggle;

    public EntitlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IExamFeeCalculatorFactory examFees,
        IEntitlementToggleResolver toggle)
    {
        _db = db;
        _currentUser = currentUser;
        _examFees = examFees;
        _toggle = toggle;
    }

    public async Task<DoctorEntitlement> ComputeForBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var breakdown = await ExplainForBatchAsync(batchId, cancellationToken);

        return await UpsertAsync(
            batchId,
            breakdown.DoctorId,
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
        Guid batchId,
        Guid doctorId,
        string system,
        decimal computedAmount,
        CancellationToken cancellationToken)
    {
        var existing = await _db.DoctorEntitlements.FirstOrDefaultAsync(e => e.BatchId == batchId, cancellationToken);

        if (existing is not null)
        {
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
            CalculationSystem = system,
            ComputedAmount = computedAmount,
        };

        _db.DoctorEntitlements.Add(entitlement);
        await _db.SaveChangesAsync(cancellationToken);
        return entitlement;
    }
}
