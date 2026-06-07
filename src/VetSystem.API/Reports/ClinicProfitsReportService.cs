using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Partnership;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Clinic-profits report (M12 task 3, PRD §7.9 + §6.8). Read-only, environment-scoped via the global
/// query filter. Net profit is the clinic's <b>gross margin</b> (ex-tax revenue − COGS); the
/// partnership distribution (M10 <see cref="IProfitDistributionService"/>) splits that net among the
/// active partners as of the period end (or today when the period is open-ended). Doctor shares are
/// surfaced as a separate line, not netted out. Revenue and COGS are taken over the window's
/// <em>effective</em> (non-void) invoices, mirroring the append-only void model — a <c>void</c> row
/// and its voided original both drop out (same rule as <c>EntitlementService</c>).
/// </summary>
public sealed class ClinicProfitsReportService
{
    private readonly ApplicationDbContext _db;
    private readonly IProfitDistributionService _distribution;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;

    public ClinicProfitsReportService(
        ApplicationDbContext db,
        IProfitDistributionService distribution,
        ICurrentUserAccessor currentUser,
        IClock clock)
    {
        _db = db;
        _distribution = distribution;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<ClinicProfitsReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var envId = _currentUser.EnvironmentId
            ?? throw new ForbiddenException("unauthenticated", "Authentication required.");

        var (start, end) = ReportQuery.ResolveWindow(from, to);

        // Effective (non-void) invoices in the window: revenue (ex-tax) + the basis for COGS.
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.IssuedAt >= start && i.IssuedAt < end)
            .Select(i => new { i.Id, i.BatchId, i.Total, i.TaxAmount, i.Status, i.VoidOfInvoiceId })
            .ToListAsync(cancellationToken);

        var voidedOriginalIds = invoices
            .Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();
        var effective = invoices
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .ToList();

        var revenue = effective.Sum(i => i.Total - i.TaxAmount); // ex-tax net sales
        var effectiveIds = effective.Select(i => i.Id).ToList();

        // M24 — settled batches (تصفية) supersede billed prices retroactively (like voids): add the
        // per-line repricing deltas of settled-batch invoices, and net out the batch-level discounts
        // granted in this window (attributed at settled_at — no per-invoice basis exists for them).
        var invoiceDeltas = await SettledPriceOverlay.LoadInvoiceDeltasAsync(
            _db,
            effective.Where(i => i.BatchId is not null).Select(i => (i.Id, i.BatchId!.Value)).ToList(),
            cancellationToken);
        var settlementDiscounts = await SettledPriceOverlay.SumDiscountsSettledInWindowAsync(
            _db, start, end, cancellationToken);
        revenue += invoiceDeltas.Values.Sum() - settlementDiscounts;

        var cogs = effectiveIds.Count == 0
            ? 0m
            : await _db.InvoiceItems.AsNoTracking()
                .Where(it => effectiveIds.Contains(it.InvoiceId) && it.ProductId != null)
                .SumAsync(it => (decimal?)(it.CostPrice * it.Quantity), cancellationToken) ?? 0m;

        var doctorShares = await _db.DoctorEntitlements.AsNoTracking()
            .Where(e => e.CreatedAt >= start && e.CreatedAt < end)
            .SumAsync(e => (decimal?)e.ComputedAmount, cancellationToken) ?? 0m;

        var netProfit = revenue - cogs; // gross-margin policy: doctor shares are a separate line, not a cost

        var asOf = to ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var split = await _distribution.DistributeAsync(netProfit, envId, asOf, cancellationToken);

        return new ClinicProfitsReportResponse(
            from,
            to,
            asOf,
            Revenue: revenue,
            Cogs: cogs,
            NetProfit: netProfit,
            DoctorShares: doctorShares,
            DistributedToPartners: split.DistributedTotal,
            RetainedByClinic: split.Retained,
            PartnerAllocations: split.Allocations,
            SettlementDiscounts: settlementDiscounts);
    }
}
