using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
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
    private readonly IClock _clock;

    public InventoryReadService(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
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
        bool? includeZeroStock,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (locationType is not null && !StockLocation.All.Contains(locationType))
        {
            throw new ConflictException("invalid_location_type", $"location_type '{locationType}' is not valid.");
        }

        var factor = 1m + (await LowStockThresholdPctAsync(cancellationToken) / 100m);

        // POS catalog view: list EVERY sellable product at one location (default warehouse), with its
        // on-hand quantity (0 when it was never received) — a LEFT JOIN, so a just-created item still
        // appears (greyed/out-of-stock at the till) instead of being invisible. Consumables are
        // excluded (M27 — they're taken out via the المستهلكات screen, never sold).
        if (includeZeroStock == true)
        {
            return await ListProductsWithStockAsync(
                locationType ?? StockLocation.Warehouse, locationId, productId, search, lowStockOnly,
                factor, skip, take, cancellationToken);
        }

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
    /// The POS catalog projection (<c>includeZeroStock=true</c>): every non-consumable product
    /// LEFT-joined to its on-hand at <paramref name="locationType"/> (default warehouse), so an
    /// unstocked product surfaces with quantity 0 (greyed/out-of-stock at the till) instead of being
    /// invisible. Same response shape + low-stock threshold semantics as the joined stock list.
    /// </summary>
    private async Task<IReadOnlyList<StockLevelResponse>> ListProductsWithStockAsync(
        string locationType,
        Guid? locationId,
        Guid? productId,
        string? search,
        bool? lowStockOnly,
        decimal factor,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        // The location id reported on zero-stock rows (one warehouse per environment by design).
        var resolvedLocationId = locationId
            ?? await _db.StockItems.AsNoTracking()
                .Where(s => s.LocationType == locationType)
                .Select(s => (Guid?)s.LocationId)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.Warehouses.AsNoTracking()
                .Select(w => (Guid?)w.Id)
                .FirstOrDefaultAsync(cancellationToken)
            ?? Guid.Empty;

        var stockAtLocation = _db.StockItems.AsNoTracking().Where(s => s.LocationType == locationType);
        if (locationId is { } lid) stockAtLocation = stockAtLocation.Where(s => s.LocationId == lid);

        var products = _db.Products.AsNoTracking().Where(p => !p.IsConsumable);
        if (productId is { } pid) products = products.Where(p => p.Id == pid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            products = products.Where(p =>
                EF.Functions.ILike(p.NameAr, pattern) ||
                (p.NameLatin != null && EF.Functions.ILike(p.NameLatin, pattern)) ||
                (p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern)));
        }

        var projected = products.Select(p => new
        {
            Product = p,
            Quantity = stockAtLocation.Where(s => s.ProductId == p.Id).Sum(s => (decimal?)s.Quantity) ?? 0m,
        });

        if (lowStockOnly == true)
        {
            projected = projected.Where(x => x.Quantity <= x.Product.ReorderPoint * factor);
        }

        return await projected
            .OrderBy(x => x.Product.NameAr)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .Select(x => new StockLevelResponse(
                x.Product.Id,
                x.Product.NameAr,
                x.Product.NameLatin,
                x.Product.Barcode,
                x.Product.Category,
                x.Product.UnitOfMeasure,
                locationType,
                resolvedLocationId,
                x.Quantity,
                x.Product.ReorderPoint,
                x.Product.ExpirationDate,
                x.Product.PurchasePrice,
                x.Product.SellingPrice,
                x.Quantity <= x.Product.ReorderPoint * factor))
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
    /// GET /inventory/lots — the FEFO lots of one product (the web's lot-detail view, the REST mirror
    /// of the field device's PowerSync <c>inventory_lots</c> read). Optional
    /// <paramref name="locationType"/>/<paramref name="locationId"/> narrow to one location; by default
    /// only on-hand lots (<c>remaining_qty &gt; 0</c>) are returned (<paramref name="onHandOnly"/> =
    /// false includes depleted lots). Ordered FEFO — earliest-expiry first, never-expiring last — the
    /// same order <see cref="VetSystem.Application.Inventory.FefoPlanner"/> consumes them.
    /// </summary>
    public async Task<IReadOnlyList<InventoryLotResponse>> ListLotsAsync(
        Guid productId,
        string? locationType,
        Guid? locationId,
        bool? onHandOnly,
        CancellationToken cancellationToken)
    {
        if (locationType is not null && !StockLocation.All.Contains(locationType))
        {
            throw new ConflictException("invalid_location_type", $"location_type '{locationType}' is not valid.");
        }

        var query = _db.InventoryLots
            .AsNoTracking()
            .Where(l => l.ProductId == productId);

        if (onHandOnly != false) query = query.Where(l => l.RemainingQty > 0m);
        if (locationType is not null) query = query.Where(l => l.LocationType == locationType);
        if (locationId is { } lid) query = query.Where(l => l.LocationId == lid);

        return await query
            .OrderBy(l => l.ExpirationDate.HasValue ? 0 : 1)    // dated lots before never-expiring
            .ThenBy(l => l.ExpirationDate)                       // earliest expiry first
            .ThenBy(l => l.ReceivedAt)                           // then oldest receipt (FIFO)
            .ThenBy(l => l.Id)                                   // stable final tie-break
            .Select(l => new InventoryLotResponse(
                l.Id,
                l.ProductId,
                l.LocationType,
                l.LocationId,
                l.PurchaseInvoiceItemId,
                l.UnitCost,
                l.ExpirationDate,
                l.LotNumber,
                l.ReceivedQty,
                l.RemainingQty,
                l.ReceivedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// GET /inventory/expiring — on-hand lots whose own expiry falls within the warning window, the
    /// lot-accurate near-expiry view (dashboard widget + the inventory alerts page). The window
    /// defaults to <c>system_settings.expiration_warning_days</c> (30 if unset); the optional
    /// <paramref name="withinDays"/> query param overrides it. This is the env-scoped REST mirror of
    /// the M11 <see cref="VetSystem.Application.Inventory.IInventoryScanService"/> scan that backs the
    /// daily <c>expiry_warning</c> notification — one row per lot, ordered soonest-expiring first.
    /// </summary>
    public async Task<IReadOnlyList<ExpiringProduct>> ListExpiringAsync(
        int? withinDays,
        CancellationToken cancellationToken)
    {
        var warningDays = withinDays ?? await ExpirationWarningDaysAsync(cancellationToken);
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var cutoff = today.AddDays(warningDays);

        var lots = await _db.InventoryLots
            .AsNoTracking()
            .Where(l => l.RemainingQty > 0m
                        && l.ExpirationDate != null
                        && l.ExpirationDate <= cutoff)
            .Join(
                _db.Products,
                l => l.ProductId,
                p => p.Id,
                (l, p) => new
                {
                    l.Id,
                    l.ProductId,
                    p.NameAr,
                    l.LotNumber,
                    ExpirationDate = l.ExpirationDate!.Value,
                    l.RemainingQty,
                })
            .ToListAsync(cancellationToken);

        if (lots.Count == 0)
        {
            return [];
        }

        var onHand = await _db.StockItems
            .AsNoTracking()
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, cancellationToken);

        return lots
            .Select(l => new ExpiringProduct(
                l.Id,
                l.ProductId,
                l.NameAr,
                l.LotNumber,
                l.ExpirationDate,
                l.ExpirationDate.DayNumber - today.DayNumber,
                l.RemainingQty,
                onHand.GetValueOrDefault(l.ProductId)))
            .OrderBy(e => e.DaysUntilExpiry)
            .ThenBy(e => e.ProductNameAr)
            .ToList();
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

    /// <summary>
    /// The environment's <c>expiration_warning_days</c> (30 if unset) — the near-expiry window. Read
    /// through the global query filter (current environment), unlike the cross-environment M11 scan.
    /// </summary>
    private async Task<int> ExpirationWarningDaysAsync(CancellationToken cancellationToken) =>
        await _db.SystemSettings
            .AsNoTracking()
            .Select(s => (int?)s.ExpirationWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? 30;
}
