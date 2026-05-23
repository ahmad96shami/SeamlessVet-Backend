using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §5 — a long-term poultry/cattle farm agreement, authored from either client (PRD §6.6):
/// Admin/Accountant in the Center Web App, or a field doctor on the mobile app for a customer
/// assigned to them. A <c>draft</c> contract is freely editable offline by its author and is
/// doctor-device authoritative; promoting it to <c>active</c> (locking the binding financial terms)
/// is gated by the <c>contracts.activate</c> permission and is an online / server-confirmed action
/// (SCHEMA "Key invariants" #7; PRD §6.6, §8.9). Once <c>active</c>+ it is server-authoritative.
/// </summary>
public sealed class Contract : Entity
{
    public Guid CustomerId { get; set; }

    /// <summary>Defaults to the authoring field doctor; also the mobile sync-scope key.</summary>
    public Guid? ResponsibleDoctorId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    public decimal? TotalPrice { get; set; }

    public int? ExpectedVisitCount { get; set; }

    public string? AnimalType { get; set; }

    public int? AnimalCount { get; set; }

    public string Status { get; set; } = ContractStatus.Draft;

    /// <summary>Author (field doctor or admin) — audit.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Who confirmed the binding terms (Draft → Active); null while draft.</summary>
    public Guid? ActivatedBy { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
}

/// <summary>
/// Lifecycle (SCHEMA §5 / PRD §6.6): <c>draft → active | cancelled</c>,
/// <c>active → completed | cancelled</c>; <c>completed</c>/<c>cancelled</c> are terminal. The
/// <c>draft → active</c> edge additionally passes through the activation gate (permission + online),
/// enforced in the service layer — the state machine here only governs which transitions are legal.
/// </summary>
public static class ContractStatus
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyCollection<string> All = [Draft, Active, Completed, Cancelled];

    /// <summary>States a contract may be created in (draft, or active for an authorized actor).</summary>
    public static readonly IReadOnlyCollection<string> Creatable = [Draft, Active];

    /// <summary>Active/Completed/Cancelled are server-authoritative on the sync path (PRD §8.4).</summary>
    public static bool IsServerAuthoritative(string status) => status is Active or Completed or Cancelled;

    public static bool CanTransition(string from, string to)
    {
        if (!All.Contains(from) || !All.Contains(to))
        {
            return false;
        }

        return from switch
        {
            Draft => to is Active or Cancelled,
            Active => to is Completed or Cancelled,
            _ => false,
        };
    }
}
