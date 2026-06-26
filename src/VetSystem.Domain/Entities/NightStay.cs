using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — a hospitalized boarding episode (مبيت, PRD §18.6). Supersedes the
/// <see cref="DailyFollowUp"/>'s billing role: the per-day clinical log still hangs under a stay
/// (via <see cref="DailyFollowUp.NightStayId"/>), but the charge lives here. Clinic-only — a stay is
/// rejected for a field visit. Nights are counted <b>hotel-style</b> against a configurable daily
/// checkout time (default 12:00; see <c>NightStayChargeCalculator</c>): <see cref="NightsCount"/> /
/// <see cref="Total"/> are stamped when the stay is closed and the <c>night_stay</c> ledger entry
/// posts (<c>nights × <see cref="NightlyRate"/></c>). <see cref="NightlyRate"/> is snapshotted from
/// <c>system_settings</c> by <see cref="CareType"/> at creation, so a later settings change never
/// re-prices an existing stay.
/// </summary>
public sealed class NightStay : Entity
{
    public Guid VisitId { get; set; }

    public string CareType { get; set; } = Entities.CareType.Medical;

    public DateTimeOffset CheckInAt { get; set; }

    /// <summary>Null while the stay is ongoing; set when the stay is closed and the charge posts.</summary>
    public DateTimeOffset? CheckOutAt { get; set; }

    /// <summary>Hotel-style nights accrued — 0 until the stay is closed.</summary>
    public int NightsCount { get; set; }

    /// <summary>Per-night cost snapshot for <see cref="CareType"/> at creation (never re-priced).</summary>
    public decimal NightlyRate { get; set; }

    /// <summary><see cref="NightsCount"/> × <see cref="NightlyRate"/> — the billed total.</summary>
    public decimal Total { get; set; }

    /// <summary>
    /// The intended discharge hour (0–23), recorded when the stay is opened. Informational only — the
    /// nights count is still computed at close against the clinic-wide checkout hour, so this never
    /// affects billing. Null = unrecorded.
    /// </summary>
    public int? ExitHour { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// Boarding care type (PRD §18.6), each with its own configurable per-night cost:
/// medical stay (مبيت علاجي), ICU stay (مبيت عناية مركزة), hotel stay (مبيت فندقي).
/// </summary>
public static class CareType
{
    public const string Medical = "medical";
    public const string Icu = "icu";
    public const string Hotel = "hotel";

    public static readonly IReadOnlyCollection<string> All = [Medical, Icu, Hotel];
}
