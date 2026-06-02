using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — one encounter, in-clinic or field (PRD §5.2). The clinical sections are all
/// nullable: a field visit fills a subset of what a hospitalized in-clinic case does.
/// <see cref="VisitNumber"/> is the human-friendly, per-user-prefixed number
/// (<c>'{users.number_prefix}-{seq}'</c>) generated client-side and UNIQUE per environment so
/// offline devices never collide (SCHEMA "Key invariants" #9). <see cref="BatchId"/> /
/// <see cref="ContractId"/> are nullable here; their FK targets land in M8.
/// </summary>
public sealed class Visit : Entity
{
    public string VisitType { get; set; } = Entities.VisitType.InClinic;

    public string? VisitNumber { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>The farm this visit attributes to (M15); null for a pet/clinic visit.</summary>
    public Guid? FarmId { get; set; }

    /// <summary>Nullable: farm visits may treat a group rather than a single pet.</summary>
    public Guid? PetId { get; set; }

    /// <summary>Field visits within a cycle (FK target added in M8).</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Field visits under a contract — enables contract pricing (FK target added in M8).</summary>
    public Guid? ContractId { get; set; }

    public Guid DoctorId { get; set; }

    /// <summary>Clinic only — who registered the visit at reception.</summary>
    public Guid? ReceptionistId { get; set; }

    public string Status { get; set; } = VisitStatus.Open;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    // A. initial assessment
    public string? ChiefComplaint { get; set; }
    public string? Symptoms { get; set; }
    public decimal? Temperature { get; set; }
    public int? HeartRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public decimal? Weight { get; set; }
    public string? ClinicalNotes { get; set; }

    // B. diagnosis
    public string? PreliminaryDiagnosis { get; set; }
    public string? FinalDiagnosis { get; set; }
    public string? Severity { get; set; }
    public string? IcdVetCode { get; set; }

    /// <summary>Field-visit exam fee (Kashfiyya) snapshot (PRD §6.2; System B input for M9).</summary>
    public decimal? ExamFeeApplied { get; set; }

    /// <summary>
    /// In-clinic checkup fee (رسوم الكشف, M17 / PRD §18.7) — auto-applied from
    /// <c>system_settings.default_checkup_fee</c> at create, editable per visit. Posts a
    /// <c>checkup_fee</c> ledger entry when the visit completes; <c>0</c> on a waived free follow-up.
    /// Null/absent on field visits.
    /// </summary>
    public decimal? CheckupFeeApplied { get; set; }

    /// <summary>
    /// The originating visit when this is a follow-up visit (M17 / PRD §18.8). Each origin grants
    /// exactly one free follow-up — the first follow-up visit it spawns has its checkup fee waived.
    /// </summary>
    public Guid? FollowUpOfVisitId { get; set; }
}

public static class VisitType
{
    public const string InClinic = "in_clinic";
    public const string Field = "field";

    public static readonly IReadOnlyCollection<string> All = [InClinic, Field];
}

/// <summary>
/// Lifecycle: <c>open → in_progress → completed | cancelled</c>. A visit becomes
/// server-authoritative once <c>completed</c>/<c>cancelled</c> (PRD §8.4); medical content is
/// doctor-device-authoritative while <c>open</c>/<c>in_progress</c>.
/// </summary>
public static class VisitStatus
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyCollection<string> All = [Open, InProgress, Completed, Cancelled];

    /// <summary>States allowed when creating a visit — it can't be born already closed.</summary>
    public static readonly IReadOnlyCollection<string> Creatable = [Open, InProgress];

    /// <summary>Terminal states are server-authoritative and reject medical-content edits.</summary>
    public static bool IsTerminal(string status) => status is Completed or Cancelled;

    /// <summary>
    /// State machine (PRD §5.2): <c>open → in_progress | completed | cancelled</c>,
    /// <c>in_progress → completed | cancelled</c>; <c>completed</c>/<c>cancelled</c> are terminal.
    /// A no-op (same state) is not a transition.
    /// </summary>
    public static bool CanTransition(string from, string to)
    {
        if (!All.Contains(from) || !All.Contains(to))
        {
            return false;
        }

        return from switch
        {
            Open => to is InProgress or Completed or Cancelled,
            InProgress => to is Completed or Cancelled,
            _ => false,
        };
    }
}

public static class Severity
{
    public const string Mild = "mild";
    public const string Moderate = "moderate";
    public const string Severe = "severe";
    public const string Critical = "critical";

    public static readonly IReadOnlyCollection<string> All = [Mild, Moderate, Severe, Critical];
}
