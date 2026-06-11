using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — what a doctor is owed, from batch supervision or a single visit (PRD §7.4, §7.8).
/// Sourced by <b>exactly one</b> of <see cref="BatchId"/> / <see cref="VisitId"/> (CHECK constraint).
///
/// <para><b>Settlement lock (SCHEMA "Key invariants" #1, PRD §7.6):</b> <see cref="Status"/> may only
/// move to <c>approved</c>/<c>paid</c> once the related customer <c>ledgers.status = 'closed'</c>.
/// Partial payments never release it — it stays <c>pending</c> until the account closes in full.
/// Enforced as a hard Application-layer guard, not a soft warning.</para>
///
/// <para>Server-authoritative on sync (M9 task 13): clients pull entitlements read-only; every write
/// (compute, approve, pay) happens through the online API, mirroring batches and stock items.</para>
/// </summary>
public sealed class DoctorEntitlement : Entity
{
    public Guid DoctorId { get; set; }

    /// <summary>Set when this entitlement is sourced by a batch (Dawra). Null for a visit-sourced one.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Set when this entitlement is sourced by a single visit. Null for a batch-sourced one.</summary>
    public Guid? VisitId { get; set; }

    /// <summary>Which mechanism produced <see cref="ComputedAmount"/> (PRD §7.4): drug_profit | direct_fee.
    /// In M28 this is accounting-only — the amount is the supervision fee under both.</summary>
    public string CalculationSystem { get; set; } = EntitlementSystem.DrugProfit;

    /// <summary>The doctor's entitlement — the supervision fee in full when the toggle is enabled, else 0
    /// (M28: no percentage, no ceiling, no clamp).</summary>
    public decimal ComputedAmount { get; set; }

    public string Status { get; set; } = EntitlementStatus.Pending;

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Disbursement method (<see cref="PaymentMethod"/>); set on pay.</summary>
    public string? PaidMethod { get; set; }
}

/// <summary>Doctor-entitlement lifecycle (PRD §7.8). Settlement lock gates pending → approved.</summary>
public static class EntitlementStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Paid = "paid";

    public static readonly IReadOnlyCollection<string> All = [Pending, Approved, Paid];
}
