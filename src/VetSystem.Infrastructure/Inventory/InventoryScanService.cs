using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;

namespace VetSystem.Infrastructure.Inventory;

/// <summary>
/// Read-only implementation of <see cref="IInventoryScanService"/>. Queries are explicitly scoped
/// to the supplied environment (the env query filter is bypassed so an M11 background worker can
/// scan every environment) and read the per-environment <c>system_settings</c> thresholds.
/// </summary>
public sealed class InventoryScanService : IInventoryScanService
{
    private readonly Persistence.ApplicationDbContext _db;
    private readonly IClock _clock;

    public InventoryScanService(Persistence.ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LowStockItem>> ScanLowStockAsync(
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        var pct = await _db.SystemSettings
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => (decimal?)s.LowStockThresholdPct)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var factor = 1m + (pct / 100m);

        var rows = await _db.StockItems
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId && s.DeletedAt == null)
            .Join(
                _db.Products.IgnoreQueryFilters().Where(p => p.DeletedAt == null),
                s => s.ProductId,
                p => p.Id,
                (s, p) => new { s, p })
            .Where(x => x.s.Quantity <= x.p.ReorderPoint * factor)
            .Select(x => new LowStockItem(
                x.p.Id,
                x.p.NameAr,
                x.p.UnitOfMeasure,
                x.s.LocationType,
                x.s.LocationId,
                x.s.Quantity,
                x.p.ReorderPoint,
                x.p.ReorderPoint * factor))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<ExpiringProduct>> ScanApproachingExpirationAsync(
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        var warningDays = await _db.SystemSettings
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => (int?)s.ExpirationWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? 30;

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var cutoff = today.AddDays(warningDays);

        var products = await _db.Products
            .IgnoreQueryFilters()
            .Where(p => p.EnvironmentId == environmentId
                        && p.DeletedAt == null
                        && p.ExpirationDate != null
                        && p.ExpirationDate <= cutoff)
            .Select(p => new { p.Id, p.NameAr, ExpirationDate = p.ExpirationDate!.Value })
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return [];
        }

        var onHand = await _db.StockItems
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId && s.DeletedAt == null)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, cancellationToken);

        return products
            .Select(p => new ExpiringProduct(
                p.Id,
                p.NameAr,
                p.ExpirationDate,
                p.ExpirationDate.DayNumber - today.DayNumber,
                onHand.GetValueOrDefault(p.Id)))
            .Where(e => e.QuantityOnHand > 0m)
            .OrderBy(e => e.DaysUntilExpiry)
            .ToList();
    }
}
