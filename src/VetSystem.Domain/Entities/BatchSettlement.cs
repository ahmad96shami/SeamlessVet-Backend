using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §5a — the batch settlement (تصفية الدورة, M24): the end-of-cycle renegotiation document.
/// When a Dawra finishes, the clinic re-agrees the per-medication unit prices across ALL the batch's
/// effective invoices and may grant a batch-level discount, then the farm account is collected and
/// closed. Invoices are never mutated (invariant #3) — this append-only document plus its ledger
/// <c>adjustment</c> entries ARE the re-pricing; everything downstream (entitlements, profit reports)
/// resolves a product's sale value through <see cref="BatchSettlementLine"/> first.
/// One settlement per batch (partial-unique on <c>batch_id</c>); settling closes the batch.
/// Online-only, no /sync surface.
/// </summary>
public sealed class BatchSettlement : Entity
{
    public Guid BatchId { get; set; }

    /// <summary>Σ per-product deltas = Σ(settled − original unit price) × qty. Usually negative.</summary>
    public decimal RepricingDelta { get; set; }

    /// <summary>Batch-level goodwill discount (خصم التصفية), ≥ 0. Reduces the farmer's debt AND the
    /// System-A doctor-share basis (client decision 2026-06-07, like the exam fee).</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Σ effective (non-void) invoice totals at settlement time — audit snapshot.</summary>
    public decimal OriginalTotal { get; set; }

    /// <summary><c>original_total + repricing_delta − discount_amount</c>, plus the supervision fee for a
    /// System-B batch (the farmer pays it on top) — what the cycle is worth after the negotiation.</summary>
    public decimal SettledTotal { get; set; }

    /// <summary>M28 — the supervision fee computed on the settled (pre-discount) revenue at settle time.
    /// Audit snapshot of the doctor's entitlement amount; for a System-B (<c>direct_fee</c>) batch it is
    /// also charged to the farmer as a <c>+supervision_fee</c> owner-ledger adjustment. Always ≥ 0.</summary>
    public decimal SupervisionFee { get; set; }

    public string? Notes { get; set; }

    public Guid SettledBy { get; set; }

    public DateTimeOffset SettledAt { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// One negotiated price: "product X sells at Y for the whole cycle". Applies uniformly to every
/// effective product line of the batch's invoices; products without a line keep their original
/// resolution (contract price else invoice unit price). Quantities/amounts are settle-time snapshots.
/// </summary>
public sealed class BatchSettlementLine : Entity
{
    public Guid SettlementId { get; set; }

    public Guid ProductId { get; set; }

    public decimal SettledUnitPrice { get; set; }

    /// <summary>Σ quantity across the batch's effective invoice lines for this product.</summary>
    public decimal OriginalQuantity { get; set; }

    /// <summary>Σ <c>line_total</c> across those lines (net of per-line discounts) — audit snapshot.</summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>Σ(settled − original unit price) × qty for this product. Per-line discounts cancel out.</summary>
    public decimal Delta { get; set; }
}
