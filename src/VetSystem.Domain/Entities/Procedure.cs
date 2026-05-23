using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a procedure performed within a <see cref="Visit"/> (PRD §5.2-C). Each links to a
/// catalog <see cref="Service"/> and snapshots its <see cref="Price"/> at the time of the visit.
/// </summary>
public sealed class Procedure : Entity
{
    public Guid VisitId { get; set; }

    public Guid? ServiceId { get; set; }

    public string? ResultText { get; set; }

    /// <summary>R2 object key for a result file (image/PDF); never a public URL.</summary>
    public string? ResultFileUrl { get; set; }

    public decimal Price { get; set; }
}
