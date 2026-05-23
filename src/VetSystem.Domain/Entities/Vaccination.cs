using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a vaccination given to a pet or a farm group (PRD §5.2, §6.7). <see cref="NextDueDate"/>
/// drives the M11 reminder job. Either <see cref="PetId"/> (a single animal) or
/// <see cref="CustomerId"/> (a farm-group vaccination) identifies the recipient.
/// </summary>
public sealed class Vaccination : Entity
{
    public Guid? PetId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? VisitId { get; set; }

    public string VaccineType { get; set; } = string.Empty;

    public DateOnly DateGiven { get; set; }

    public DateOnly? NextDueDate { get; set; }

    /// <summary>R2 object key for the printable certificate; never a public URL.</summary>
    public string? CertificateUrl { get; set; }
}
