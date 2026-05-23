using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Entitlements;

/// <summary>
/// The settlement lock (SCHEMA "Key invariants" #1, PRD §7.6) as a <b>hard</b> guard: a doctor
/// entitlement may only move to <c>approved</c>/<c>paid</c> once the related customer ledger is
/// <c>closed</c>. Partial payments never release it — the account must close in full first. Throwing
/// (not warning) is what makes the rule unbypassable.
/// </summary>
public interface ISettlementLockGuard
{
    /// <summary>Throws <see cref="ConflictException"/> (<c>settlement_locked</c>) unless the ledger is closed.</summary>
    void EnsureReleasable(string ledgerStatus);
}

/// <inheritdoc cref="ISettlementLockGuard"/>
public sealed class SettlementLockGuard : ISettlementLockGuard
{
    public void EnsureReleasable(string ledgerStatus)
    {
        if (ledgerStatus != LedgerStatus.Closed)
        {
            throw new ConflictException(
                "settlement_locked",
                "The customer account is not closed. A doctor entitlement cannot be approved or paid until "
                + "the account is settled in full and closed (partial payments do not release it).");
        }
    }
}
