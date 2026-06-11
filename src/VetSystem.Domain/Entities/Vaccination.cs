using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a vaccination given to a pet or a farm group (PRD §5.2, §6.7). <see cref="NextDueDate"/>
/// drives the M11 reminder job. Either <see cref="PetId"/> (a single animal) or
/// <see cref="CustomerId"/> (a farm-group vaccination) identifies the recipient. M26 — a catalog-linked
/// vaccination (<see cref="ProductId"/> set, a stock product of category <c>vaccine</c>) deducts stock
/// <b>FEFO</b> on administration (stock moves when recorded; mirrors a billable in-clinic med) and
/// bills once as a <b>product line</b> at issuance; <see cref="VaccineType"/> snapshots the catalog
/// name. Legacy free-text rows (<see cref="ProductId"/> null) are records only — no stock, no bill.
/// </summary>
public sealed class Vaccination : Entity
{
    public Guid? PetId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? VisitId { get; set; }

    /// <summary>M26 — the catalog vaccine (a <c>products</c> row, category <c>vaccine</c>); null = legacy free-text.</summary>
    public Guid? ProductId { get; set; }

    public string VaccineType { get; set; } = string.Empty;

    /// <summary>Price snapshot at recording time (like <see cref="Procedure.Price"/>); null = legacy row.</summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// M26 — the lot-accurate FEFO weighted-average unit cost captured when the vaccine's stock was
    /// deducted at administration (mirrors <see cref="Prescription.ResolvedUnitCost"/>). The issuance
    /// assembler snapshots this onto the invoice line's <c>cost_price</c> instead of re-deducting
    /// (the stock — and therefore the COGS — already moved). Null for free-text records and for rows
    /// recorded without a deduction. Server-managed; clients never write it.
    /// </summary>
    public decimal? ResolvedUnitCost { get; set; }

    public DateOnly DateGiven { get; set; }

    public DateOnly? NextDueDate { get; set; }

    /// <summary>R2 object key for the printable certificate; never a public URL.</summary>
    public string? CertificateUrl { get; set; }
}
