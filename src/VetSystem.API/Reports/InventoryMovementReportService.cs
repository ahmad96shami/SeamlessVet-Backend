using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Inventory-movement report (M12 task 9, PRD §7.9). Read-only, environment-scoped. Aggregates
/// <c>inventory_movements</c> over the window into per-(location, product) inflows/outflows, attributing
/// each delta to its affected location (the <c>to</c> side when positive, the <c>from</c> side when
/// negative — so a two-leg transfer counts as an outflow at the source and an inflow at the
/// destination). <see cref="InventoryMovementRow.Balance"/> is the current materialized
/// <c>stock_items.quantity</c>; over an all-time window inflows − outflows equals it (the M4 invariant,
/// re-checked by task 18).
/// </summary>
public sealed class InventoryMovementReportService
{
    private readonly ApplicationDbContext _db;

    public InventoryMovementReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryMovementReportResponse> BuildAsync(
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

        var movementQuery = _db.InventoryMovements.AsNoTracking().Where(m => m.CreatedAt >= start && m.CreatedAt < end);
        if (productId is { } p) movementQuery = movementQuery.Where(m => m.ProductId == p);

        var movements = await movementQuery
            .Select(m => new
            {
                m.ProductId,
                m.QuantityDelta,
                m.FromLocationType,
                m.FromLocationId,
                m.ToLocationType,
                m.ToLocationId,
            })
            .ToListAsync(cancellationToken);

        // Aggregate by affected location (to-side for inflows, from-side for outflows) + product.
        var groups = new Dictionary<(string LocationType, Guid LocationId, Guid ProductId), (decimal Inflows, decimal Outflows)>();
        foreach (var m in movements)
        {
            var affectedType = m.QuantityDelta < 0 ? m.FromLocationType : m.ToLocationType;
            var affectedId = m.QuantityDelta < 0 ? m.FromLocationId : m.ToLocationId;
            if (affectedType is null || affectedId is null)
            {
                continue; // a valid movement always populates the sign-appropriate side; skip malformed rows
            }

            var key = (affectedType, affectedId.Value, m.ProductId);
            groups.TryGetValue(key, out var acc);
            if (m.QuantityDelta >= 0)
            {
                acc.Inflows += m.QuantityDelta;
            }
            else
            {
                acc.Outflows += -m.QuantityDelta;
            }

            groups[key] = acc;
        }

        IEnumerable<KeyValuePair<(string LocationType, Guid LocationId, Guid ProductId), (decimal Inflows, decimal Outflows)>> filtered = groups;
        if (locationType is not null) filtered = filtered.Where(kv => kv.Key.LocationType == locationType);
        if (locationId is { } lid) filtered = filtered.Where(kv => kv.Key.LocationId == lid);
        var keyed = filtered.ToList();

        // Current balances from stock_items for the same (location, product) keys.
        var stockQuery = _db.StockItems.AsNoTracking().AsQueryable();
        if (productId is { } sp) stockQuery = stockQuery.Where(s => s.ProductId == sp);
        if (locationType is not null) stockQuery = stockQuery.Where(s => s.LocationType == locationType);
        if (locationId is { } slid) stockQuery = stockQuery.Where(s => s.LocationId == slid);
        var balances = (await stockQuery
                .Select(s => new { s.LocationType, s.LocationId, s.ProductId, s.Quantity })
                .ToListAsync(cancellationToken))
            .ToDictionary(s => (s.LocationType, s.LocationId, s.ProductId), s => s.Quantity);

        var allRows = keyed
            .Select(kv =>
            {
                var balance = balances.TryGetValue(kv.Key, out var q) ? q : 0m;
                return new InventoryMovementRow(
                    kv.Key.LocationType,
                    kv.Key.LocationId,
                    kv.Key.ProductId,
                    kv.Value.Inflows,
                    kv.Value.Outflows,
                    kv.Value.Inflows - kv.Value.Outflows,
                    balance);
            })
            .OrderBy(r => r.LocationType)
            .ThenBy(r => r.LocationId)
            .ThenBy(r => r.ProductId)
            .ToList();

        var page = allRows
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .ToList();

        return new InventoryMovementReportResponse(from, to, productId, locationType, locationId, allRows.Count, page);
    }
}
