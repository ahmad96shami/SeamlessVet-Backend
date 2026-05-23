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
                Reason: request.Reason),
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
