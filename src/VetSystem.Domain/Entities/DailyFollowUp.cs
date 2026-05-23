using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a daily entry for a hospitalized case (PRD §5.2-E). Clinic-only: rejected for
/// field visits (M5 task 11).
/// </summary>
public sealed class DailyFollowUp : Entity
{
    public Guid VisitId { get; set; }

    public DateOnly EntryDate { get; set; }

    public string? Condition { get; set; }

    public decimal? Temperature { get; set; }

    public int? HeartRate { get; set; }

    public int? RespiratoryRate { get; set; }

    public string? AdministeredMeds { get; set; }

    public string? Notes { get; set; }
}
