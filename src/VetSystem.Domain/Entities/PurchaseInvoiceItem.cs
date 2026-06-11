using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M19 (SCHEMA §4) — one line of a <see cref="PurchaseInvoice"/>. Always a product (purchases are
/// goods, never services). <see cref="UnitCost"/> is the cost the clinic paid at receipt and is
/// snapshotted on the invoice — it is the supplier-side mirror of
/// <see cref="InvoiceItem.CostPrice"/> on the sales side.
/// </summary>
public sealed class PurchaseInvoiceItem : Entity
{
    public Guid PurchaseInvoiceId { get; set; }

    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; } = 1m;

    /// <summary>The per-unit cost paid to the supplier (snapshot at receipt).</summary>
    public decimal UnitCost { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    /// <summary>M25 — expiry of the goods received on this line; carried onto the created lot for FEFO.</summary>
    public DateOnly? ExpirationDate { get; set; }
}
