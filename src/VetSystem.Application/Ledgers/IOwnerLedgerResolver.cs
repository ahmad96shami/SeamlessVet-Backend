namespace VetSystem.Application.Ledgers;

/// <summary>
/// Resolves the ledger a charge posts to under the M16 per-farm routing: the farm ledger when a
/// <c>farm_id</c> is in play, else the customer ledger, else <c>null</c> (a walk-in with no owner —
/// the caller skips the ledger). The owning ledger always exists (a customer gets one on creation,
/// a farm in its PUT path); a missing one is a <c>NotFoundException</c>. Shared by the invoicing,
/// night-stay (مبيت) and checkup-fee paths so the routing rule lives in one place.
/// </summary>
public interface IOwnerLedgerResolver
{
    Task<Guid?> ResolveAsync(Guid? customerId, Guid? farmId, CancellationToken cancellationToken);
}
