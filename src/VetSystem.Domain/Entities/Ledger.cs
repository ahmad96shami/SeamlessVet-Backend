using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §2 — one per customer. <see cref="Balance"/> and <see cref="Status"/> are derived
/// from <see cref="LedgerEntry"/> rows only; never written directly from a CRUD path.
/// Positive balance = customer owes the clinic. Closing the account is what releases doctor
/// entitlements (SCHEMA "Key invariants" #1, M9).
/// </summary>
public sealed class Ledger : Entity
{
    public Guid CustomerId { get; set; }

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
