using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M25 (SCHEMA §4) — one received lot of a product at a single location, carrying the cost paid and
/// the expiry for that specific batch of goods. FEFO costing consumes lots
/// earliest-<see cref="ExpirationDate"/> first; <see cref="RemainingQty"/> is the materialized
/// remainder (decremented in the same transaction as the consuming movement), so per
/// (location, product) <c>Σ remaining_qty == StockItem.quantity</c> — the lot-level mirror of the
/// <see cref="StockItem"/> invariant. A lot is created on a <c>receive</c> (a purchase-invoice line),
/// a positive <c>adjust</c>, a <c>return_add</c>, or the destination leg of a transfer (which mirrors
/// the consumed source lot's cost + expiry so field stock keeps its FEFO basis).
/// </summary>
public sealed class InventoryLot : Entity
{
    public Guid ProductId { get; set; }

    public string LocationType { get; set; } = StockLocation.Warehouse;

    public Guid LocationId { get; set; }

    /// <summary>The purchase-invoice line this lot was received from; null for adjust/return/transfer lots.</summary>
    public Guid? PurchaseInvoiceItemId { get; set; }

    /// <summary>Per-unit cost paid at receipt (snapshot) — the basis for FEFO COGS.</summary>
    public decimal UnitCost { get; set; }

    public DateOnly? ExpirationDate { get; set; }

    /// <summary>Optional free-text lot / batch number from the supplier.</summary>
    public string? LotNumber { get; set; }

    /// <summary>Quantity received into this lot (immutable once set).</summary>
    public decimal ReceivedQty { get; set; }

    /// <summary>Materialized remaining quantity on hand for this lot; FEFO decrements it.</summary>
    public decimal RemainingQty { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}
