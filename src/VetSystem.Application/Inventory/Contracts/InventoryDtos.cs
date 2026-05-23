namespace VetSystem.Application.Inventory.Contracts;

/// <summary>
/// Normalized input to <c>IInventoryService.ApplyMovementAsync</c> — the single chokepoint for
/// every stock change (SCHEMA "Key invariants" #2). Built by the dedicated <c>/inventory/*</c>
/// endpoints and by <c>/sync/inventory_movements</c>. <see cref="Quantity"/> is a positive
/// magnitude for every type except <c>adjust</c>, where it is a signed delta. Locations follow
/// the operation's semantics (e.g. <c>load_to_field</c>: From = warehouse, To = field). The
/// translator turns this into one signed movement row (single-location types) or two
/// (<c>load_to_field</c>/<c>unload_from_field</c> transfers).
/// </summary>
public sealed record MovementIntent(
    Guid? Id,
    string MovementType,
    Guid ProductId,
    decimal Quantity,
    string? FromLocationType,
    Guid? FromLocationId,
    string? ToLocationType,
    Guid? ToLocationId,
    string IdempotencyKey,
    string? Reason = null,
    Guid? VisitId = null,
    Guid? InvoiceId = null);

/// <summary>The resulting balance of a single (location, product) stock item after a movement.</summary>
public sealed record StockBalance(
    string LocationType,
    Guid LocationId,
    Guid ProductId,
    decimal Quantity);

/// <summary>
/// Outcome of an applied movement. <see cref="MovementId"/> is the primary row (the first/only
/// leg); <see cref="MovementIds"/> lists every row written (two for a transfer). <see cref="Balances"/>
/// carries each affected location's new quantity.
/// </summary>
public sealed record MovementResult(
    Guid MovementId,
    IReadOnlyList<Guid> MovementIds,
    IReadOnlyList<StockBalance> Balances,
    bool Replayed);

public sealed record StockItemResponse(
    Guid Id,
    string LocationType,
    Guid LocationId,
    Guid ProductId,
    decimal Quantity,
    DateTimeOffset UpdatedAt);

public sealed record InventoryMovementResponse(
    Guid Id,
    Guid ProductId,
    string MovementType,
    string? FromLocationType,
    Guid? FromLocationId,
    string? ToLocationType,
    Guid? ToLocationId,
    decimal QuantityDelta,
    string? Reason,
    Guid? VisitId,
    Guid? InvoiceId,
    Guid PerformedBy,
    DateTimeOffset CreatedAt);

// ---- Dedicated /inventory/* endpoint requests (Admin / Inventory staff; online-preferred) ----

/// <summary>Purchase-order receive into a warehouse (defaults to the environment's central one).</summary>
public sealed record ReceiveStockRequest(
    Guid? Id,
    Guid ProductId,
    decimal Quantity,
    Guid? WarehouseId,
    string IdempotencyKey,
    string? Reason = null);

/// <summary>Signed adjustment at a single location, with a mandatory reason.</summary>
public sealed record AdjustStockRequest(
    Guid? Id,
    Guid ProductId,
    string LocationType,
    Guid LocationId,
    decimal QuantityDelta,
    string Reason,
    string IdempotencyKey);

/// <summary>Move stock from the central warehouse into a field doctor's inventory.</summary>
public sealed record LoadFieldRequest(
    Guid? Id,
    Guid ProductId,
    Guid FieldInventoryId,
    decimal Quantity,
    Guid? WarehouseId,
    string IdempotencyKey,
    string? Reason = null);

/// <summary>Return stock from a field doctor's inventory back to the central warehouse.</summary>
public sealed record UnloadFieldRequest(
    Guid? Id,
    Guid ProductId,
    Guid FieldInventoryId,
    decimal Quantity,
    Guid? WarehouseId,
    string IdempotencyKey,
    string? Reason = null);
