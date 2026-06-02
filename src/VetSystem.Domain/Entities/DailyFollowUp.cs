using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a daily entry for a hospitalized case (PRD §5.2-E). Clinic-only: rejected for
/// field visits (M5 task 11). From M17 a daily entry can hang under a <see cref="NightStay"/>
/// (مبيت) via <see cref="NightStayId"/>; the per-day clinical log is retained, the billing moves
/// to the stay (PRD §18.6).
/// </summary>
public sealed class DailyFollowUp : Entity
{
    public Guid VisitId { get; set; }

    /// <summary>The boarding episode this daily entry belongs to (M17); null for a non-boarding entry.</summary>
    public Guid? NightStayId { get; set; }

    public DateOnly EntryDate { get; set; }

    public string? Condition { get; set; }

    public decimal? Temperature { get; set; }

    public int? HeartRate { get; set; }

    public int? RespiratoryRate { get; set; }

    public string? AdministeredMeds { get; set; }

    public string? Notes { get; set; }
}
