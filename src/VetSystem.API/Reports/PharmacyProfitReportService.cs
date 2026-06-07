using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Pharmacy-profit report (M20 task 1, PRD §7.9). Read-only, environment-scoped via the global query
/// filter. Sums the product lines (<c>invoice_items.product_id != null</c>) of the window's effective
/// (non-void) invoices: revenue from <c>line_total</c>, cost from the <c>cost_price</c> snapshot × qty.
/// The void model is honoured exactly as in <see cref="ClinicProfitsReportService"/> — a <c>void</c> row
/// and its voided original both drop out — so <see cref="PharmacyProfitReportResponse.Cost"/> reconciles
/// to the clinic-profits COGS on the same window. The per-product breakdown is offset-paged; the summary
/// totals span the whole filtered set regardless of paging.
/// </summary>
public sealed class PharmacyProfitReportService
{
    private readonly ApplicationDbContext _db;

    public PharmacyProfitReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PharmacyProfitReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, int? skip, int? take, CancellationToken cancellationToken)
    {
        var (start, end) = ReportQuery.ResolveWindow(from, to);

        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.IssuedAt >= start && i.IssuedAt < end)
            .Select(i => new { i.Id, i.BatchId, i.Status, i.VoidOfInvoiceId })
            .ToListAsync(cancellationToken);

        var voidedOriginalIds = invoices
            .Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();
        var effective = invoices
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .ToList();
        var effectiveIds = effective.Select(i => i.Id).ToList();

        if (effectiveIds.Count == 0)
        {
            return new PharmacyProfitReportResponse(from, to, 0m, 0m, 0m, 0, []);
        }

        // M24 — a settled batch's product lines are worth their negotiated price, retroactively
        // (invariant #11). The maps force the grouping in memory; cost stays the frozen snapshot.
        var priceMaps = await SettledPriceOverlay.LoadPriceMapsAsync(
            _db,
            effective.Where(i => i.BatchId is not null).Select(i => i.BatchId!.Value).Distinct().ToList(),
            cancellationToken);
        var batchByInvoice = effective
            .Where(i => i.BatchId is not null)
            .ToDictionary(i => i.Id, i => i.BatchId!.Value);

        // Per-product totals over the product lines of the effective invoices.
        var items = await _db.InvoiceItems.AsNoTracking()
            .Where(it => effectiveIds.Contains(it.InvoiceId) && it.ProductId != null)
            .Select(it => new
            {
                it.InvoiceId,
                ProductId = it.ProductId!.Value,
                it.Quantity,
                it.DiscountAmount,
                it.LineTotal,
                it.CostPrice,
            })
            .ToListAsync(cancellationToken);

        var grouped = items
            .GroupBy(it => it.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x =>
                    batchByInvoice.TryGetValue(x.InvoiceId, out var batchId)
                    && priceMaps.TryGetValue(batchId, out var prices)
                    && prices.TryGetValue(g.Key, out var settled)
                        ? SettledPriceOverlay.OverlaidLineRevenue(settled, x.Quantity, x.DiscountAmount)
                        : x.LineTotal),
                Cost = g.Sum(x => x.CostPrice * x.Quantity),
            })
            .ToList();

        var totalRevenue = grouped.Sum(x => x.Revenue);
        var totalCost = grouped.Sum(x => x.Cost);

        var ordered = grouped
            .OrderByDescending(x => x.Revenue - x.Cost)
            .ThenBy(x => x.ProductId)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .ToList();

        var pageIds = ordered.Select(x => x.ProductId).ToList();
        var names = await _db.Products.AsNoTracking()
            .Where(p => pageIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NameAr })
            .ToDictionaryAsync(p => p.Id, p => p.NameAr, cancellationToken);

        var rows = ordered
            .Select(x => new PharmacyProfitRow(
                x.ProductId,
                names.TryGetValue(x.ProductId, out var name) ? name : x.ProductId.ToString(),
                x.Quantity,
                x.Revenue,
                x.Cost,
                x.Revenue - x.Cost))
            .ToList();

        return new PharmacyProfitReportResponse(
            from, to, totalRevenue, totalCost, totalRevenue - totalCost, grouped.Count, rows);
    }
}
