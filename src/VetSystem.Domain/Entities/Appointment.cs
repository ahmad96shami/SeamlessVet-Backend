using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §7 — a scheduled encounter (PRD §5.3). Most foreign keys are nullable: a slot can be
/// blocked for a doctor before the customer/pet is known. Lifecycle is
/// <c>scheduled → confirmed → attended | no_show | cancelled</c>
/// (see <see cref="AppointmentStatus"/>). When an appointment is marked <c>attended</c> the server
/// opens a clinic <see cref="Visit"/> and records its id in <see cref="VisitId"/> so the two are
/// linked (M6 task 6). Conflict detection occupies a half-open <c>[scheduled_at, +duration)</c>
/// window per doctor — see <see cref="AppointmentSchedule"/>.
/// </summary>
public sealed class Appointment : Entity
{
    public Guid? CustomerId { get; set; }

    public Guid? PetId { get; set; }

    public Guid? DoctorId { get; set; }

    public Guid? ServiceId { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>Slot length in minutes. Null falls back to <see cref="AppointmentSchedule.DefaultDurationMin"/>.</summary>
    public int? DurationMin { get; set; }

    public string Status { get; set; } = AppointmentStatus.Scheduled;

    public string? Notes { get; set; }

    /// <summary>The clinic visit opened when this appointment was attended (M6); null until then.</summary>
    public Guid? VisitId { get; set; }
}

/// <summary>
/// Lifecycle (PRD §5.3): <c>scheduled → confirmed | attended | no_show | cancelled</c>,
/// <c>confirmed → attended | no_show | cancelled</c>; <c>attended</c>/<c>no_show</c>/<c>cancelled</c>
/// are terminal. Terminal transitions go through dedicated endpoints (<c>/attend</c>, <c>/cancel</c>,
/// <c>/no-show</c>) so they are auditable single actions, never a side effect of a PATCH.
/// </summary>
public static class AppointmentStatus
{
    public const string Scheduled = "scheduled";
    public const string Confirmed = "confirmed";
    public const string Attended = "attended";
    public const string NoShow = "no_show";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyCollection<string> All = [Scheduled, Confirmed, Attended, NoShow, Cancelled];

    /// <summary>States allowed at creation — an appointment can't be born already attended/cancelled.</summary>
    public static readonly IReadOnlyCollection<string> Creatable = [Scheduled, Confirmed];

    /// <summary>
    /// Statuses that occupy the doctor's slot for conflict detection. A <c>cancelled</c> or
    /// <c>no_show</c> appointment frees the slot, so a new overlapping booking is allowed.
    /// </summary>
    public static readonly IReadOnlyCollection<string> OccupiesSlot = [Scheduled, Confirmed, Attended];

    /// <summary>Terminal states reject further transitions and PATCH edits.</summary>
    public static bool IsTerminal(string status) => status is Attended or NoShow or Cancelled;

    /// <summary>
    /// State machine: <c>scheduled → confirmed | attended | no_show | cancelled</c>,
    /// <c>confirmed → attended | no_show | cancelled</c>. A no-op (same state) is not a transition.
    /// </summary>
    public static bool CanTransition(string from, string to)
    {
        if (!All.Contains(from) || !All.Contains(to))
        {
            return false;
        }

        return from switch
        {
            Scheduled => to is Confirmed or Attended or NoShow or Cancelled,
            Confirmed => to is Attended or NoShow or Cancelled,
            _ => false,
        };
    }
}

/// <summary>
/// Pure scheduling math for appointment conflict detection (M6 task 3). Each appointment occupies a
/// <b>half-open</b> interval <c>[start, start + duration)</c>: two slots that merely touch
/// (one ends exactly when the next begins) do <b>not</b> conflict, while any positive overlap does.
/// Kept dependency-free in the Domain so the boundary cases are unit-testable without a database.
/// </summary>
public static class AppointmentSchedule
{
    /// <summary>Slot length used when an appointment leaves <c>duration_min</c> unset.</summary>
    public const int DefaultDurationMin = 30;

    /// <summary>
    /// Largest accepted slot length (24h). Bounds the conflict-detection look-back: an existing
    /// appointment can only overlap a new window if it started no earlier than
    /// <c>window_start − MaxDurationMin</c>, which lets the DB query stay index-bounded.
    /// </summary>
    public const int MaxDurationMin = 24 * 60;

    /// <summary>The exclusive end of the slot starting at <paramref name="start"/>.</summary>
    public static DateTimeOffset EndOf(DateTimeOffset start, int? durationMin)
        => start.AddMinutes(durationMin ?? DefaultDurationMin);

    /// <summary>
    /// True when half-open intervals <c>[aStart, aEnd)</c> and <c>[bStart, bEnd)</c> overlap.
    /// Back-to-back slots (<c>aEnd == bStart</c>) return false; a one-minute overlap returns true.
    /// </summary>
    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd)
        => aStart < bEnd && bStart < aEnd;
}
