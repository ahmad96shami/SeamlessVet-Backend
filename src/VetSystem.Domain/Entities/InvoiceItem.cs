using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — one invoice line, referencing a product <b>or</b> a service (typed pair + CHECK).
/// <see cref="CostPrice"/> is snapshotted from <c>products.purchase_price</c> at sale time and never
/// recomputed (SCHEMA "Key invariants" #8); it feeds the M9 batch drug-profit calc. The optional
/// <see cref="PrescriptionId"/> / <see cref="ProcedureId"/> / <see cref="VaccinationId"/> back-links
/// record that this line bills a specific <c>dispensed_to_owner</c> prescription, a visit procedure,
/// or a catalog-linked vaccination, so the issuance assembler (M7 task 8) never bills the same
/// source twice.
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
}
