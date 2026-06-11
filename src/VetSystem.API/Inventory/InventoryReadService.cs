using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Application.Reports;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Inventory;

/// <summary>
/// Read-only inventory queries for the Center Web App. The web reads on-hand balances and the
/// movement history over plain REST <c>GET</c>s (the mobile app reads the same data via PowerSync,
/// so these were never required server-side until the web arrived — BACKEND_PREREQS §2). All three
/// are <b>authenticated</b> (no extra permission — inventory staff hold <c>inventory.adjust</c>, not
/// <c>reports.read</c>), environment-scoped by the global query filter, and offset-paged like the
/// other admin tables. Read-only: <c>stock_items</c> stays server-authoritative — no write path here.
/// </summary>
public sealed class InventoryReadService
{
    private readonly ApplicationDbContext _db;

    public InventoryReadService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /inventory/stock — current on-hand per (product, location), a <c>stock_items ⋈ products</c>
    /// projection. Optional <paramref name="locationType"/>/<paramref name="locationId"/>/
    /// <paramref name="productId"/> filters, case-insensitive <paramref name="search"/> over
    /// name/barcode, and <paramref name="lowStockOnly"/> which keeps only rows at or below the
    /// low-stock threshold (reorder point inflated by <c>system_settings.low_stock_threshold_pct</c>,
    /// the same definition as <see cref="StockLevelResponse.BelowReorderPoint"/> and the M11 scan).
    /// </summary>
    public async Task<IReadOnlyList<StockLevelResponse>> ListStockAsync(
        string? locationType,
        Guid? locationId,
        Guid? productId,
        string? search,
        bool? lowStockOnly,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (locationType is not null && !StockLocation.All.Contains(locationType))
        {
            throw new ConflictException("invalid_location_type", $"location_type '{locationType}' is not valid.");
        }

        var factor = 1m + (await LowStockThresholdPctAsync(cancellationToken) / 100m);

        var query = _db.StockItems
            .AsNoTracking()
            .Join(_db.Products, s => s.ProductId, p => p.Id, (s, p) => new { s, p });

        if (locationType is not null) query = query.Where(x => x.s.LocationType == locationType);
        if (locationId is { } lid) query = query.Where(x => x.s.LocationId == lid);
        if (productId is { } pid) query = query.Where(x => x.s.ProductId == pid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.p.NameAr, pattern) ||
                (x.p.NameLatin != null && EF.Functions.ILike(x.p.NameLatin, pattern)) ||
                (x.p.Barcode != null && EF.Functions.ILike(x.p.Barcode, pattern)));
        }

        if (lowStockOnly == true)
        {
            query = query.Where(x => x.s.Quantity <= x.p.ReorderPoint * factor);
        }

        return await query
            .OrderBy(x => x.p.NameAr)
            .ThenBy(x => x.s.LocationType)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .Select(x => new StockLevelResponse(
                x.p.Id,
                x.p.NameAr,
                x.p.NameLatin,
                x.p.Barcode,
                x.p.Category,
                x.p.UnitOfMeasure,
                x.s.LocationType,
                x.s.LocationId,
                x.s.Quantity,
                x.p.ReorderPoint,
                x.p.ExpirationDate,
                x.p.PurchasePrice,
                x.p.SellingPrice,
                x.s.Quantity <= x.p.ReorderPoint * factor))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// GET /inventory/movements — the append-only movement history (SCHEMA §4), newest first.
    /// Optional <paramref name="productId"/>/<paramref name="movementType"/> filters, a
    /// <paramref name="locationType"/>/<paramref name="locationId"/> filter that matches either the
    /// from- or to-side (so a transfer shows under both its legs' locations), and a
    /// <c>[from, to]</c> day window. Shares the projection with the <c>reports.read</c>-gated
    /// inventory-movement report — this is just the per-row, operationally-gated view of the same data.
    /// </summary>
    public async Task<IReadOnlyList<InventoryMovementResponse>> ListMovementsAsync(
        Guid? productId,
        string? locationType,
        Guid? locationId,
        string? movementType,
        DateOnly? from,
        DateOnly? to,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (locationType is not null && !StockLocation.All.Contains(locationType))
        {
            throw new ConflictException("invalid_location_type", $"location_type '{locationType}' is not valid.");
        }

        if (movementType is not null && !MovementType.All.Contains(movementType))
        {
            throw new ConflictException("invalid_movement_type", $"movement_type '{movementType}' is not valid.");
        }

        var (start, end) = ReportQuery.ResolveWindow(from, to);

        var query = _db.InventoryMovements
            .AsNoTracking()
            .Where(m => m.CreatedAt >= start && m.CreatedAt < end);

        if (productId is { } pid) query = query.Where(m => m.ProductId == pid);
        if (movementType is not null) query = query.Where(m => m.MovementType == movementType);
        if (locationType is not null)
        {
            query = query.Where(m => m.FromLocationType == locationType || m.ToLocationType == locationType);
        }

        if (locationId is { } lid)
        {
            query = query.Where(m => m.FromLocationId == lid || m.ToLocationId == lid);
        }

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .Select(m => new InventoryMovementResponse(
                m.Id,
                m.ProductId,
                m.MovementType,
                m.FromLocationType,
                m.FromLocationId,
                m.ToLocationType,
                m.ToLocationId,
                m.QuantityDelta,
                m.Reason,
                m.VisitId,
                m.InvoiceId,
                m.LotId,
                m.PerformedBy,
                m.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// GET /inventory/field-inventories — every field doctor's "moving warehouse" in the environment,
    /// the picker source for the web load/unload flows. Unpaged: there is one per field doctor, so the
    /// set is naturally small. Ordered by doctor name.
    /// </summary>
    public async Task<IReadOnlyList<FieldInventoryResponse>> ListFieldInventoriesAsync(
        CancellationToken cancellationToken)
    {
        return await _db.FieldInventories
            .AsNoTracking()
            .Join(_db.Users, f => f.DoctorId, u => u.Id, (f, u) => new { f, u })
            .OrderBy(x => x.u.FullName)
            .Select(x => new FieldInventoryResponse(x.f.Id, x.f.DoctorId, x.u.FullName))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The environment's <c>low_stock_threshold_pct</c> (0 if unset). Read through the global query
    /// filter (current environment), unlike the cross-environment M11 scan.
    /// </summary>
    private async Task<decimal> LowStockThresholdPctAsync(CancellationToken cancellationToken) =>
        await _db.SystemSettings
            .AsNoTracking()
            .Select(s => (decimal?)s.LowStockThresholdPct)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;
}
