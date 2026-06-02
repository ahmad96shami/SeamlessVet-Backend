using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §2 — a customer's or a farm's running account (M16: polymorphic owner). Exactly one of
/// <see cref="CustomerId"/> / <see cref="FarmId"/> is set (DB <c>ck_ledgers_owner</c>): a customer
/// ledger holds pet/clinic charges, a farm ledger holds that farm's field charges. <see cref="Balance"/>
/// and <see cref="Status"/> are derived from <see cref="LedgerEntry"/> rows only; never written
/// directly from a CRUD path. Positive balance = the owner owes the clinic. Closing the owning ledger
/// is what releases the doctor entitlements sourced from it (SCHEMA "Key invariants" #1, M9 → M16).
/// </summary>
public sealed class Ledger : Entity
{
    /// <summary>Set for a customer-owned ledger (pet/clinic charges); null for a farm ledger.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Set for a farm-owned ledger (M16); null for a customer ledger.</summary>
    public Guid? FarmId { get; set; }

    public decimal Balance { get; set; }

    public string Status { get; set; } = LedgerStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }
}

public static class LedgerStatus
{
    public const string Open = "open";
    public const string HasDebt = "has_debt";
    public const string Closed = "closed";

    public static readonly IReadOnlyCollection<string> All = [Open, HasDebt, Closed];
}
