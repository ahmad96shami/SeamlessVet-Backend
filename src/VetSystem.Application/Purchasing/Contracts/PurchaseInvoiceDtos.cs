namespace VetSystem.Application.Purchasing.Contracts;

/// <summary>
/// One line of a purchase invoice (M19, SCHEMA §4). Always a product. <c>UnitCost</c> is the per-unit
/// cost the clinic paid the supplier and is snapshotted on the line at receipt. M25 — the optional
/// <c>ExpirationDate</c> + <c>LotNumber</c> ride onto the <c>inventory_lot</c> the receive creates,
/// so this line's goods consume FEFO at their own cost + expiry.
/// </summary>
public sealed record PurchaseInvoiceLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount = 0m,
    DateOnly? ExpirationDate = null,
    string? LotNumber = null);

/// <summary>
/// Purchase-invoice issuance (M19 task 5). One transaction (a) writes a signed <c>receive</c> movement
/// per line into the warehouse, (b) snapshots each unit cost, and (c) posts the full
/// <c>Total</c> as a payable to the supplier ledger. <c>WarehouseId</c> defaults to the environment's
/// central warehouse. <c>TaxAmount</c> is the supplier's input VAT (optional; not derived from the
/// clinic's sales tax). <c>IdempotencyKey</c> is the row-level dedupe.
/// </summary>
public sealed record PurchaseInvoiceRequest(
    Guid? Id,
    Guid SupplierId,
    Guid? WarehouseId,
    string? Number,
    decimal DiscountAmount,
    decimal? TaxAmount,
    IReadOnlyList<PurchaseInvoiceLineRequest> Items,
    string? Notes,
    string IdempotencyKey);

public sealed record PurchaseInvoiceItemResponse(
    Guid Id,
    Guid PurchaseInvoiceId,
    Guid ProductId,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal LineTotal,
    DateOnly? ExpirationDate);

public sealed record PurchaseInvoiceResponse(
    Guid Id,
    Guid SupplierId,
    Guid WarehouseId,
    string? Number,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    Guid ReceivedBy,
    DateTimeOffset ReceivedAt,
    string? Notes,
    IReadOnlyList<PurchaseInvoiceItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
