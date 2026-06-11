using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Inventory;

/// <summary>
/// Online-preferred inventory operations (PRD §8.9, M4 tasks 6–9): receive, adjust, and the
/// two-leg load/unload transfers between the central warehouse and a field doctor's inventory.
/// Thin over <see cref="IInventoryService"/> — it resolves the default warehouse and shapes the
/// typed request into a <see cref="MovementIntent"/>; the delta math, negative-stock guard, and
/// idempotency all live in the Application/Infrastructure service.
/// </summary>
public sealed class InventoryAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryService _inventory;

    public InventoryAdminService(ApplicationDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<MovementResult> ReceiveAsync(ReceiveStockRequest request, CancellationToken cancellationToken)
    {
        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        return await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: request.Id,
                MovementType: MovementType.Receive,
                ProductId: request.ProductId,
                Quantity: request.Quantity,
                FromLocationType: null,
                FromLocationId: null,
                ToLocationType: StockLocation.Warehouse,
                ToLocationId: warehouseId,
                IdempotencyKey: request.IdempotencyKey,
                Reason: request.Reason,
                // M25 — seed the created lot's cost + expiry (cost falls back to catalog purchase price).
                UnitCost: request.UnitCost,
                ExpirationDate: request.ExpirationDate,
                LotNumber: request.LotNumber),
            cancellationToken);
    }

    public async Task<MovementResult> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken)
    {
        return await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: request.Id,
                MovementType: MovementType.Adjust,
                ProductId: request.ProductId,
                Quantity: request.QuantityDelta, // signed
                FromLocationType: null,
                FromLocationId: null,
                ToLocationType: request.LocationType, // adjust target; translator handles negative sign
                ToLocationId: request.LocationId,
                IdempotencyKey: request.IdempotencyKey,
                Reason: request.Reason),
            cancellationToken);
    }

    public async Task<MovementResult> LoadFieldAsync(LoadFieldRequest request, CancellationToken cancellationToken)
    {
        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        return await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: request.Id,
                MovementType: MovementType.LoadToField,
                ProductId: request.ProductId,
                Quantity: request.Quantity,
                FromLocationType: StockLocation.Warehouse,
                FromLocationId: warehouseId,
                ToLocationType: StockLocation.Field,
                ToLocationId: request.FieldInventoryId,
                IdempotencyKey: request.IdempotencyKey,
                Reason: request.Reason),
            cancellationToken);
    }

    public async Task<MovementResult> UnloadFieldAsync(UnloadFieldRequest request, CancellationToken cancellationToken)
    {
        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        return await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: request.Id,
                MovementType: MovementType.UnloadFromField,
                ProductId: request.ProductId,
                Quantity: request.Quantity,
                FromLocationType: StockLocation.Field,
                FromLocationId: request.FieldInventoryId,
                ToLocationType: StockLocation.Warehouse,
                ToLocationId: warehouseId,
                IdempotencyKey: request.IdempotencyKey,
                Reason: request.Reason),
            cancellationToken);
    }

    /// <summary>
    /// M27 — record internal-use consumption of a consumable as a single negative <c>consume</c>
    /// movement (FEFO-deducted via the M25 engine, which snapshots the consumed cost onto
    /// <c>inventory_movements.unit_cost</c> for the consumables report). The product must carry the
    /// <c>is_consumable</c> flag (mirrors M26's <c>product_not_vaccine</c> link guard); a non-consumable
    /// write-off goes through <c>adjust</c> instead. Location defaults to the central warehouse.
    /// </summary>
    public async Task<MovementResult> ConsumeAsync(ConsumeStockRequest request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .Select(p => new { p.Id, p.IsConsumable })
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("product", request.ProductId);

        if (!product.IsConsumable)
        {
            throw new ConflictException("product_not_consumable",
                "Only a product flagged as consumable can be consumed; use a stock adjustment for other write-offs.");
        }

        var (locationType, locationId) = await ResolveConsumeLocationAsync(
            request.LocationType, request.LocationId, cancellationToken);

        return await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: request.Id,
                MovementType: MovementType.Consume,
                ProductId: request.ProductId,
                Quantity: request.Quantity,
                FromLocationType: locationType,
                FromLocationId: locationId,
                ToLocationType: null,
                ToLocationId: null,
                IdempotencyKey: request.IdempotencyKey,
                Reason: request.Reason),
            cancellationToken);
    }

    /// <summary>
    /// Resolves the location a consumption deducts from: the environment's central warehouse when the
    /// caller supplies neither part, else the explicit (type, id) pair (existence validated downstream).
    /// The "both or neither" rule is also enforced by <c>ConsumeStockRequestValidator</c>.
    /// </summary>
    private async Task<(string LocationType, Guid LocationId)> ResolveConsumeLocationAsync(
        string? locationType, Guid? locationId, CancellationToken cancellationToken)
    {
        if (locationType is null && locationId is null)
        {
            return (StockLocation.Warehouse, await ResolveWarehouseIdAsync(null, cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(locationType) || locationId is not { } id || id == Guid.Empty)
        {
            throw new ConflictException("invalid_location",
                "Consume requires both location_type and location_id, or neither (defaults to the central warehouse).");
        }

        return (locationType, id);
    }

    /// <summary>
    /// Resolves the warehouse for a movement. An explicit id wins (its existence is validated
    /// downstream); otherwise the environment's single central warehouse is used. Errors clearly
    /// when none exists, or when several do and the caller did not disambiguate.
    /// </summary>
    private async Task<Guid> ResolveWarehouseIdAsync(Guid? supplied, CancellationToken cancellationToken)
    {
        if (supplied is { } id && id != Guid.Empty)
        {
            return id;
        }

        var warehouseIds = await _db.Warehouses
            .AsNoTracking()
            .Select(w => w.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return warehouseIds.Count switch
        {
            0 => throw new ConflictException("no_warehouse",
                "No warehouse exists in this environment; seed one before receiving stock."),
            1 => warehouseIds[0],
            _ => throw new ConflictException("warehouse_id_required",
                "Multiple warehouses exist in this environment; specify warehouse_id."),
        };
    }
}
