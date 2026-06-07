using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Simplified P&amp;L (M12 task 8, PRD §7.9). Read-only, environment-scoped. Same financial basis as the
/// clinic-profits report (effective non-void invoices over the window): ex-tax revenue, tax collected,
/// COGS from the cost_price snapshots, and the doctor-shares memo. Gross profit = revenue − COGS (the
/// gross-margin policy: tax and doctor shares are memo lines, not netted into profit).
/// </summary>
public sealed class ProfitAndLossReportService
{
    private readonly ApplicationDbContext _db;

    public ProfitAndLossReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ProfitAndLossResponse> BuildAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = ReportQuery.ResolveWindow(from, to);

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

        var revenue = effective.Sum(i => i.Total - i.TaxAmount);
        var taxCollected = effective.Sum(i => i.TaxAmount);
        var effectiveIds = effective.Select(i => i.Id).ToList();

        // M24 — same settlement overlay as clinic-profits (the two must reconcile): per-line
        // repricing deltas retroactively, batch discounts at settled_at.
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

        return new ProfitAndLossResponse(
            from, to, revenue, taxCollected, cogs, revenue - cogs, doctorShares,
            SettlementDiscounts: settlementDiscounts);
    }
}
