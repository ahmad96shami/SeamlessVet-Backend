using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts;
using VetSystem.Application.Entitlements;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Entitlements;

/// <summary>
/// Computes <see cref="DoctorEntitlement"/> rows (M9 task 8). The pure calculators (exam-fee models,
/// System A, System B) and the toggle resolver do the arithmetic; this service supplies their inputs
/// from the database — the batch config, its (non-void) invoices and line cost snapshots, and the
/// contract-overridden <c>sale_value</c> via <see cref="IPricingService"/> (SCHEMA invariant #8).
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
    private readonly IPricingService _pricing;
    private readonly IExamFeeCalculatorFactory _examFees;
    private readonly IEntitlementToggleResolver _toggle;
    private readonly ISystemADrugProfitCalculator _systemA;
    private readonly ISystemBDirectFeeCalculator _systemB;

    public EntitlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IPricingService pricing,
        IExamFeeCalculatorFactory examFees,
        IEntitlementToggleResolver toggle,
        ISystemADrugProfitCalculator systemA,
        ISystemBDirectFeeCalculator systemB)
    {
        _db = db;
        _currentUser = currentUser;
        _pricing = pricing;
        _examFees = examFees;
        _toggle = toggle;
        _systemA = systemA;
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
            new EntitlementAmount(breakdown.DoctorShare, breakdown.CeilingApplied),
            cancellationToken);
    }

    public async Task<BatchEntitlementBreakdown> ExplainForBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
                    ?? throw new NotFoundException("batch", batchId);

        var (effectiveInvoices, productItems) = await LoadEffectiveInvoicesAsync(
            i => i.BatchId == batchId, cancellationToken);
        var revenue = effectiveInvoices.Values.Sum(i => i.Total);

        // sale_value (task 6): the contract-overridden price where an active contract applies on the
        // invoice date, else the line's snapshotted unit_price. cost is the line's cost_price snapshot.
        var lines = new List<DrugProfitLine>(productItems.Count);
        var drugCost = 0m;
        foreach (var item in productItems)
        {
            var asOf = DateOnly.FromDateTime(effectiveInvoices[item.InvoiceId].IssuedAt.UtcDateTime);
            var resolved = await _pricing.ResolveUnitPriceAsync(item.ProductId!.Value, batch.CustomerId, asOf, cancellationToken);
            var saleValue = resolved.IsContractPrice ? resolved.UnitPrice : item.UnitPrice;
            lines.Add(new DrugProfitLine(saleValue, item.CostPrice, item.Quantity));
            drugCost += item.CostPrice * item.Quantity;
        }
        var drugProfit = lines.Sum(l => (l.SaleValue - l.Cost) * l.Quantity);

        var examFee = _examFees.For(batch.SupervisionFeeModel)
            .Calculate(new ExamFeeBasis(batch.SupervisionFeeValue, batch.AnimalCount, revenue));

        var enabled = _toggle.IsEnabled(batch.EntitlementEnabled, await GlobalToggleAsync(cancellationToken));

        string system;
        EntitlementAmount amount;
        if (!enabled)
        {
            // Disabled ⇒ all profit to the clinic (invariant #4). Record a 0 row for audit.
            system = batch.EntitlementSystem ?? EntitlementSystem.DrugProfit;
            amount = new EntitlementAmount(0m, null);
        }
        else
        {
            system = batch.EntitlementSystem
                     ?? throw new ConflictException("entitlement_system_unset",
                         $"Batch '{batchId}' has the entitlement system enabled but no entitlement_system set.");
            amount = system switch
            {
                EntitlementSystem.DrugProfit => _systemA.Calculate(
                    new SystemAInput(lines, examFee, batch.DoctorSharePercent ?? 0m, batch.DoctorShareCeiling)),
                EntitlementSystem.DirectFee => _systemB.Calculate(examFee),
                _ => throw new ConflictException("invalid_entitlement_system", $"Unknown entitlement system '{system}'."),
            };
        }

        // Under System A the doctor's share comes out of drug profit, so the clinic keeps the rest;
        // under System B / when disabled the doctor's fee is paid separately, so the clinic keeps all of it.
        var clinicShare = system == EntitlementSystem.DrugProfit
            ? drugProfit - amount.ComputedAmount
            : drugProfit;

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
            examFee,
            amount.ComputedAmount,
            amount.CeilingApplied,
            clinicShare);
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
        var amount = enabled ? _systemB.Calculate(fee) : new EntitlementAmount(0m, null);

        return await UpsertAsync(
            e => e.VisitId == visitId,
            visit.DoctorId,
            batchId: null,
            visitId: visitId,
            EntitlementSystem.DirectFee,
            amount,
            cancellationToken);
    }

    /// <summary>
    /// Loads a source's invoices net of voids: a voided original (status still <c>issued</c>, append-only)
    /// is dropped along with its <c>void</c> reversal row, so neither its totals nor its line items count.
    /// Returns the surviving invoices keyed by id, plus their product line items.
    /// </summary>
    private async Task<(Dictionary<Guid, Invoice> Invoices, List<InvoiceItem> ProductItems)> LoadEffectiveInvoicesAsync(
        System.Linq.Expressions.Expression<Func<Invoice, bool>> predicate, CancellationToken cancellationToken)
    {
        var all = await _db.Invoices.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);

        var voidedOriginalIds = all.Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();

        var effective = all
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .ToDictionary(i => i.Id);

        if (effective.Count == 0)
        {
            return (effective, []);
        }

        var ids = effective.Keys.ToList();
        var productItems = await _db.InvoiceItems.AsNoTracking()
            .Where(it => ids.Contains(it.InvoiceId) && it.ProductId != null)
            .ToListAsync(cancellationToken);

        return (effective, productItems);
    }

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
        EntitlementAmount amount,
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
            existing.ComputedAmount = amount.ComputedAmount;
            existing.CeilingApplied = amount.CeilingApplied;
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
            ComputedAmount = amount.ComputedAmount,
            CeilingApplied = amount.CeilingApplied,
            Status = EntitlementStatus.Pending,
        };

        _db.DoctorEntitlements.Add(entitlement);
        await _db.SaveChangesAsync(cancellationToken);
        return entitlement;
    }
}
