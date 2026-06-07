using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — one invoice line, referencing a product <b>or</b> a service (typed pair + CHECK).
/// <see cref="CostPrice"/> is snapshotted from <c>products.purchase_price</c> at sale time and never
/// recomputed (SCHEMA "Key invariants" #8); it feeds the M9 batch drug-profit calc. The optional
/// <see cref="PrescriptionId"/> / <see cref="ProcedureId"/> / <see cref="VaccinationId"/> /
/// <see cref="NightStayId"/> / <see cref="CheckupFeeVisitId"/> back-links record that this line
/// bills a specific visit charge — a billable prescription, a procedure, a catalog-linked
/// vaccination, a night stay (M23), or the visit's checkup fee (M23) — so the issuance assembler
/// (M7 task 8) never bills the same source twice.
/// </summary>
public sealed class InvoiceItem : Entity
{
    public Guid InvoiceId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? ServiceId { get; set; }

    public string? Description { get; set; }

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    /// <summary>Snapshot of <c>products.purchase_price</c> at sale time; 0 for service lines.</summary>
    public decimal CostPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    /// <summary>Set when this line bills a <c>dispensed_to_owner</c> prescription (M7 task 8).</summary>
    public Guid? PrescriptionId { get; set; }

    /// <summary>Set when this line bills a visit procedure (auto-assembled at issuance).</summary>
    public Guid? ProcedureId { get; set; }

    /// <summary>Set when this line bills a catalog-linked visit vaccination (auto-assembled at issuance).</summary>
    public Guid? VaccinationId { get; set; }

    /// <summary>Set when this line bills a closed night stay (M23 — quantity = nights, price = rate).</summary>
    public Guid? NightStayId { get; set; }

    /// <summary>Set when this line bills the visit's in-clinic checkup fee (M23 — at most one per visit).</summary>
    public Guid? CheckupFeeVisitId { get; set; }
}
