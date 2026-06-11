using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Consumables report (M27, PRD §8). Read-only, environment-scoped via the global query filter. Sums
/// the window's <c>consume</c> <c>inventory_movements</c> (internal use of <c>is_consumable</c>
/// products) per (location, product): <see cref="ConsumablesReportRow.Quantity"/> is the consumed
/// magnitude (−<c>quantity_delta</c>) and <see cref="ConsumablesReportRow.Cost"/> is its FEFO cost
/// (<c>Σ qty × unit_cost</c>, the per-leg weighted-average snapshotted at consume time). A consume
/// movement is a single negative leg, so its affected location is the <c>from</c> side. The per-row
/// breakdown is offset-paged; the totals span the whole filtered set regardless of paging.
/// </summary>
public sealed class ConsumablesReportService
{
    private readonly ApplicationDbContext _db;

    public ConsumablesReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ConsumablesReportResponse> BuildAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? productId,
        string? locationType,
        Guid? locationId,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (locationType is not null && !StockLocation.All.Contains(locationType))
        {
            throw new ConflictException("invalid_location_type", $"location_type '{locationType}' is not valid.");
        }

        var (start, end) = ReportQuery.ResolveWindow(from, to);

        var query = _db.InventoryMovements.AsNoTracking()
            .Where(m => m.MovementType == MovementType.Consume && m.CreatedAt >= start && m.CreatedAt < end);
        if (productId is { } p) query = query.Where(m => m.ProductId == p);
        if (locationType is not null) query = query.Where(m => m.FromLocationType == locationType);
        if (locationId is { } lid) query = query.Where(m => m.FromLocationId == lid);

        var movements = await query
            .Select(m => new
            {
                m.ProductId,
                m.FromLocationType,
                m.FromLocationId,
                m.QuantityDelta,
                m.UnitCost,
            })
            .ToListAsync(cancellationToken);

        // A consume movement always populates its from side (single negative leg); skip any malformed row.
        var grouped = movements
            .Where(m => m.FromLocationType is not null && m.FromLocationId is not null)
            .GroupBy(m => (m.ProductId, LocationType: m.FromLocationType!, LocationId: m.FromLocationId!.Value))
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.LocationType,
                g.Key.LocationId,
                Quantity = g.Sum(x => -x.QuantityDelta),
                Cost = g.Sum(x => -x.QuantityDelta * (x.UnitCost ?? 0m)),
            })
            .ToList();

        var totalQuantity = grouped.Sum(x => x.Quantity);
        var totalCost = grouped.Sum(x => x.Cost);

        var ordered = grouped
            .OrderByDescending(x => x.Cost)
            .ThenBy(x => x.LocationType)
            .ThenBy(x => x.ProductId)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .ToList();

        var pageIds = ordered.Select(x => x.ProductId).Distinct().ToList();
        var names = await _db.Products.AsNoTracking()
            .Where(p => pageIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NameAr })
            .ToDictionaryAsync(p => p.Id, p => p.NameAr, cancellationToken);

        var rows = ordered
            .Select(x => new ConsumablesReportRow(
                x.ProductId,
                names.TryGetValue(x.ProductId, out var name) ? name : x.ProductId.ToString(),
                x.LocationType,
                x.LocationId,
                x.Quantity,
                x.Cost))
            .ToList();

        return new ConsumablesReportResponse(
            from, to, productId, locationType, locationId, totalQuantity, totalCost, grouped.Count, rows);
    }
}
