using VetSystem.Application.SupplierLedgers.Contracts;

namespace VetSystem.Application.SupplierLedgers;

/// <summary>
/// M19 (SCHEMA §4) — the only path that creates <c>supplier_ledger_entries</c>. INSERT-only;
/// corrections are new <c>adjustment</c> rows, never updates or deletes. The implementation atomically
/// computes <c>balance_after</c> from the current balance, then bumps
/// <see cref="VetSystem.Domain.Entities.SupplierLedger.Balance"/> and transitions
/// <see cref="VetSystem.Domain.Entities.SupplierLedger.Status"/> (open ⇄ has_debt) in the same
/// SaveChanges. Mirrors <c>ILedgerService</c> but kept distinct so the customer/farm settlement-lock
/// code is untouched. Used by the purchase-invoice and supplier-payment flows.
/// </summary>
public interface ISupplierLedgerService
{
    /// <summary>
    /// Appends a supplier-ledger entry. Idempotency is enforced via the
    /// <c>ux_supplier_ledger_entries_env_idempotency</c> unique index; a duplicate idempotency key
    /// returns the original row instead of failing.
    /// </summary>
    Task<SupplierLedgerEntryResponse> AppendEntryAsync(
        SupplierLedgerEntryRequest request, CancellationToken cancellationToken);

    /// <summary>Returns the supplier's full statement over the optional date window.</summary>
    Task<SupplierStatementResponse> GetStatementAsync(
        Guid supplierId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}
