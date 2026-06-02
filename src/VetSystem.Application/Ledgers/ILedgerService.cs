using VetSystem.Application.Ledgers.Contracts;

namespace VetSystem.Application.Ledgers;

/// <summary>
/// SCHEMA "Key invariants" #3 — the only path that creates <c>ledger_entries</c>. INSERT-only;
/// corrections are new <c>adjustment</c> rows, never updates or deletes. Implementation atomically
/// computes <c>balance_after</c> from the current ledger balance, then bumps
/// <see cref="VetSystem.Domain.Entities.Ledger.Balance"/> and transitions
/// <see cref="VetSystem.Domain.Entities.Ledger.Status"/> (open ⇄ has_debt) in the same SaveChanges.
/// Used by M3 directly (statement adjustments, sync handler) and by M7 invoices /
/// receipt-vouchers / void-compensations.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Appends a ledger entry. Idempotency is enforced via the
    /// <c>ux_ledger_entries_env_idempotency</c> unique index; a duplicate
    /// <see cref="LedgerEntryRequest.IdempotencyKey"/> returns the original row instead of failing.
    /// Throws <see cref="VetSystem.Domain.Common.NotFoundException"/> when the ledger does not exist.
    /// </summary>
    Task<LedgerEntryResponse> AppendEntryAsync(LedgerEntryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the customer's full statement over the optional date window. <c>opening_balance</c>
    /// is the running balance at <c>from</c> (or 0 if no <c>from</c>); <c>closing_balance</c> is
    /// the balance after the last entry in range. Used by M3 task 8 and M12 reports.
    /// </summary>
    Task<StatementResponse> GetStatementAsync(
        Guid customerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken);

    /// <summary>
    /// M16 — the same statement for a farm's ledger (<c>GET /farms/{id}/statement</c>). The owning
    /// customer rides along in <see cref="StatementResponse.CustomerId"/>/<c>CustomerName</c>; the
    /// farm is identified by <see cref="StatementResponse.FarmId"/>/<c>FarmName</c>.
    /// </summary>
    Task<StatementResponse> GetFarmStatementAsync(
        Guid farmId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken);
}
