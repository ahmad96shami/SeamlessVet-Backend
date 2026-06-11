namespace VetSystem.Application.Inventory.Contracts;

/// <summary>
/// Normalized input to <c>IInventoryService.ApplyMovementAsync</c> — the single chokepoint for
/// every stock change (SCHEMA "Key invariants" #2). Built by the dedicated <c>/inventory/*</c>
/// endpoints and by <c>/sync/inventory_movements</c>. <see cref="Quantity"/> is a positive
/// magnitude for every type except <c>adjust</c>, where it is a signed delta. Locations follow
/// the operation's semantics (e.g. <c>load_to_field</c>: From = warehouse, To = field). The
/// translator turns this into one signed movement row (single-location types) or two
/// (<c>load_to_field</c>/<c>unload_from_field</c> transfers).
///
/// <para>M25 — lot fields drive FEFO costing. <see cref="UnitCost"/> / <see cref="ExpirationDate"/>
/// / <see cref="LotNumber"/> / <see cref="PurchaseInvoiceItemId"/> are carried onto the lot a
/// stock-arriving movement (<c>receive</c> / positive <c>adjust</c> / <c>return_add</c>) creates;
/// they are ignored by stock-leaving and transfer movements (a transfer mirrors the source lots'
/// cost + expiry). <see cref="UnitCost"/> falls back to the product's catalog purchase price when
/// not supplied.</para>
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
    Guid? InvoiceId = null,
    Guid? PurchaseInvoiceId = null,
    decimal? UnitCost = null,
    DateOnly? ExpirationDate = null,
    string? LotNumber = null,
    Guid? PurchaseInvoiceItemId = null);

/// <summary>The resulting balance of a single (location, product) stock item after a movement.</summary>
public sealed record StockBalance(
    string LocationType,
    Guid LocationId,
    Guid ProductId,
    decimal Quantity);

/// <summary>
/// Outcome of an applied movement. <see cref="MovementId"/> is the primary row (the first/only
/// leg); <see cref="MovementIds"/> lists every row written (two for a transfer). <see cref="Balances"/>
/// carries each affected location's new quantity. M25 — <see cref="ResolvedUnitCost"/> is the
/// lot-accurate per-unit cost basis the caller snapshots as COGS: the FEFO weighted-average of the
/// lots a stock-leaving movement consumed (<c>sale_deduct</c> / negative <c>adjust</c> / a
/// transfer's source), or the received cost of the lot a stock-arriving movement created. It is
/// <c>0</c> on an idempotent replay (the original snapshot already stands; no recompute).
/// </summary>
public sealed record MovementResult(
    Guid MovementId,
    IReadOnlyList<Guid> MovementIds,
    IReadOnlyList<StockBalance> Balances,
    bool Replayed,
    decimal ResolvedUnitCost);

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
    Guid? LotId,
    Guid PerformedBy,
    DateTimeOffset CreatedAt);

// ---- Web read projections (the Center Web App reads on-hand + movements over REST; the mobile
//      app reads the same data via PowerSync). Authenticated, env-scoped, offset-paged. ----

/// <summary>
/// A current on-hand row for the web stock list and alert views (GET /inventory/stock) — a
/// <c>stock_items ⋈ products</c> projection at one location. <see cref="ReorderPoint"/> and
/// <see cref="ExpirationDate"/> are the product's; <see cref="BelowReorderPoint"/> is the low-stock
/// flag, computed with the <b>same threshold the M11 scan + alerts use</b> —
/// <c>quantity ≤ reorder_point × (1 + system_settings.low_stock_threshold_pct/100)</c> — so
/// "low stock" means one thing system-wide (see <see cref="LowStockItem"/>).
/// </summary>
public sealed record StockLevelResponse(
    Guid ProductId,
    string NameAr,
    string? NameLatin,
    string? Barcode,
    string Category,
    string? UnitOfMeasure,
    string LocationType,
    Guid LocationId,
    decimal Quantity,
    decimal ReorderPoint,
    DateOnly? ExpirationDate,
    decimal PurchasePrice,
    decimal SellingPrice,
    bool BelowReorderPoint);

/// <summary>
/// A field doctor's inventory (GET /inventory/field-inventories) — the picker source for the
/// web load/unload flows. <see cref="Id"/> is the field-inventory id passed as the load/unload
/// <c>FieldInventoryId</c>; <see cref="DoctorName"/> is the owning doctor's full name.
/// </summary>
public sealed record FieldInventoryResponse(
    Guid Id,
    Guid DoctorId,
    string DoctorName);

// ---- Dedicated /inventory/* endpoint requests (Admin / Inventory staff; online-preferred) ----

/// <summary>
/// Purchase-order receive into a warehouse (defaults to the environment's central one). M25 — the
/// optional <see cref="UnitCost"/> / <see cref="ExpirationDate"/> / <see cref="LotNumber"/> seed the
/// FEFO lot the receive creates; <see cref="UnitCost"/> falls back to the product's catalog purchase
/// price when omitted.
/// </summary>
public sealed record ReceiveStockRequest(
    Guid? Id,
    Guid ProductId,
    decimal Quantity,
    Guid? WarehouseId,
    string IdempotencyKey,
    string? Reason = null,
    decimal? UnitCost = null,
    DateOnly? ExpirationDate = null,
    string? LotNumber = null);

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

// ---- Scan results (data only; M11 turns these into alerts via Hangfire) ----

/// <summary>
/// A (location, product) stock item at or below its low-stock threshold. Threshold =
/// <c>reorder_point × (1 + system_settings.low_stock_threshold_pct/100)</c>.
/// </summary>
public sealed record LowStockItem(
    Guid ProductId,
    string ProductNameAr,
    string? UnitOfMeasure,
    string LocationType,
    Guid LocationId,
    decimal Quantity,
    decimal ReorderPoint,
    decimal ThresholdQuantity);

/// <summary>
/// M25 — a single on-hand <c>inventory_lot</c> whose expiry falls within
/// <c>system_settings.expiration_warning_days</c> (<see cref="DaysUntilExpiry"/> is negative if
/// already expired). One row per lot: <see cref="NearExpiryQuantity"/> is that lot's remaining
/// quantity; <see cref="QuantityOnHand"/> is the product's total on hand across all locations, so
/// the alert reads "X of Y units expiring".
/// </summary>
public sealed record ExpiringProduct(
    Guid LotId,
    Guid ProductId,
    string ProductNameAr,
    string? LotNumber,
    DateOnly ExpirationDate,
    int DaysUntilExpiry,
    decimal NearExpiryQuantity,
    decimal QuantityOnHand);
