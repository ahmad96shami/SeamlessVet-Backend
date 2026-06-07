using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a vaccination given to a pet or a farm group (PRD §5.2, §6.7). <see cref="NextDueDate"/>
/// drives the M11 reminder job. Either <see cref="PetId"/> (a single animal) or
/// <see cref="CustomerId"/> (a farm-group vaccination) identifies the recipient. A catalog-linked
/// vaccination (<see cref="ServiceId"/> set, mirroring <see cref="Procedure"/>) is billable: the
/// issuance assembler bills it once as a service line; <see cref="VaccineType"/> snapshots the
/// catalog name. Legacy free-text rows (<see cref="ServiceId"/> null) are records only.
/// </summary>
public sealed class Vaccination : Entity
{
    public Guid? PetId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? VisitId { get; set; }

    /// <summary>The catalog vaccine (a <c>services</c> row, category "vaccination"); null = legacy free-text.</summary>
    public Guid? ServiceId { get; set; }

    public string VaccineType { get; set; } = string.Empty;

    /// <summary>Price snapshot at recording time (like <see cref="Procedure.Price"/>); null = legacy row.</summary>
    public decimal? Price { get; set; }

    public DateOnly DateGiven { get; set; }

    public DateOnly? NextDueDate { get; set; }

    /// <summary>R2 object key for the printable certificate; never a public URL.</summary>
    public string? CertificateUrl { get; set; }
}
