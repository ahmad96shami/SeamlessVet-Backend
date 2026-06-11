using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §4 — append-only audit trail of every stock change, always expressed as a signed delta
/// (PRD §8.4: "always send +5 or -2"). Never UPDATEd or DELETEd; corrections are new movement rows.
///
/// <para><b>Delta sign + affected location.</b> <see cref="QuantityDelta"/> is signed. A row's
/// <i>affected location</i> — the one whose <see cref="StockItem"/> balance the delta is applied to —
/// is <see cref="ToLocationType"/>/<see cref="ToLocationId"/> when the delta is positive (stock
/// arriving) and <see cref="FromLocationType"/>/<see cref="FromLocationId"/> when negative (stock
/// leaving). Single-location movements populate only the relevant side
/// (<c>receive</c>/<c>return_add</c> → <c>to</c>; <c>sale_deduct</c> → <c>from</c>; <c>adjust</c> →
/// whichever matches the sign).</para>
///
/// <para><b>Two-leg transfers.</b> <c>load_to_field</c> and <c>unload_from_field</c> each write
/// <b>two</b> rows in one transaction — a negative-delta leg at the source and a positive-delta leg
/// at the destination — both carrying <c>from</c>=source and <c>to</c>=destination for audit. This
/// keeps the per-location invariant <c>stock_items.quantity == Σ quantity_delta</c> (over rows whose
/// affected location is that location) literally true.</para>
/// </summary>
public sealed class InventoryMovement : Entity
{
    public Guid ProductId { get; set; }

    public string MovementType { get; set; } = Entities.MovementType.Adjust;

    public string? FromLocationType { get; set; }

    public Guid? FromLocationId { get; set; }

    public string? ToLocationType { get; set; }

    public Guid? ToLocationId { get; set; }

    /// <summary>Signed delta. Positive = stock into <c>to</c>; negative = stock out of <c>from</c>.</summary>
    public decimal QuantityDelta { get; set; }

    public string? Reason { get; set; }

    public Guid? VisitId { get; set; }

    public Guid? InvoiceId { get; set; }

    /// <summary>M19 — set on a <c>receive</c> leg written by a purchase invoice; links stock to its source.</summary>
    public Guid? PurchaseInvoiceId { get; set; }

    /// <summary>M25 — the lot this leg created (receive / transfer credit) or drew from (single-lot
    /// deduction); null when a deduction split across multiple lots (FEFO).</summary>
    public Guid? LotId { get; set; }

    /// <summary>M27 — the FEFO weighted-average per-unit cost this leg consumed, snapshotted on a
    /// <see cref="MovementType.Consume"/> movement so the consumables report values consumption
    /// (qty × unit_cost) without re-deriving already-decremented lots. Null on every other movement
    /// type (a sale's COGS still snapshots onto <c>invoice_items.cost_price</c>).</summary>
    public decimal? UnitCost { get; set; }

    public Guid PerformedBy { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class MovementType
{
    public const string Receive = "receive";
    public const string Adjust = "adjust";
    public const string LoadToField = "load_to_field";
    public const string UnloadFromField = "unload_from_field";
    public const string SaleDeduct = "sale_deduct";
    public const string ReturnAdd = "return_add";

    /// <summary>M27 — internal-use consumption of a consumable (gloves, syringes, …): a single
    /// negative-delta leg at one location that FEFO-consumes lots like a sale, but represents
    /// internal use rather than a sale (no invoice). Carries the consumed FEFO cost on
    /// <see cref="InventoryMovement.UnitCost"/> for the consumables report.</summary>
    public const string Consume = "consume";

    public static readonly IReadOnlyCollection<string> All =
    [
        Receive, Adjust, LoadToField, UnloadFromField, SaleDeduct, ReturnAdd, Consume,
    ];
}
