using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M19 (SCHEMA §4) — a goods-receipt invoice from a supplier. Issuing one is a single transaction
/// that (a) writes a signed <c>receive</c> <see cref="InventoryMovement"/> per line into the warehouse
/// (delta-only, SCHEMA "Key invariants" #2), (b) snapshots each line's <see cref="PurchaseInvoiceItem.UnitCost"/>,
/// and (c) posts the full <see cref="Total"/> as a <c>purchase_invoice</c> payable to the supplier's
/// <see cref="SupplierLedger"/>. Append-only: a wrong invoice is corrected with a manual supplier-ledger
/// <c>adjustment</c> (and a compensating stock movement), never an UPDATE/DELETE.
/// <see cref="Number"/> is the supplier's own free-text invoice reference (optional).
/// </summary>
public sealed class PurchaseInvoice : Entity
{
    public Guid SupplierId { get; set; }

    /// <summary>The warehouse the goods were received into (defaults to the environment's central one).</summary>
    public Guid WarehouseId { get; set; }

    /// <summary>The supplier's own invoice reference (free text); not the per-user-prefixed sales number.</summary>
    public string? Number { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    public Guid ReceivedBy { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Notes { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
