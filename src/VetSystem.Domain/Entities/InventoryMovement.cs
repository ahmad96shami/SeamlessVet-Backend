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

    public static readonly IReadOnlyCollection<string> All =
    [
        Receive, Adjust, LoadToField, UnloadFromField, SaleDeduct, ReturnAdd,
    ];
}
