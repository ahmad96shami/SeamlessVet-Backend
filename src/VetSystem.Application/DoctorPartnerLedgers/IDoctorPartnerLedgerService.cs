using VetSystem.Application.DoctorPartnerLedgers.Contracts;

namespace VetSystem.Application.DoctorPartnerLedgers;

/// <summary>
/// M30 (SCHEMA §4) — the only path that creates <c>doctor_partner_ledger_entries</c>. INSERT-only;
/// corrections are new <c>adjustment</c> rows, never updates or deletes. The implementation atomically
/// computes <c>balance_after</c> from the current balance, then bumps
/// <see cref="VetSystem.Domain.Entities.DoctorPartnerLedger.Balance"/> and transitions
/// <see cref="VetSystem.Domain.Entities.DoctorPartnerLedger.Status"/> (open ⇄ has_debt) in the same
/// SaveChanges. Mirrors <c>ISupplierLedgerService</c> but on the doctor-payable side. Used by the batch
/// settlement (entitlement credit) and doctor-partner payment flows.
/// </summary>
public interface IDoctorPartnerLedgerService
{
    /// <summary>
    /// Appends a doctor-partner-ledger entry. Idempotency is enforced via the
    /// <c>ux_doctor_partner_ledger_entries_env_idempotency</c> unique index; a duplicate idempotency key
    /// returns the original row instead of failing.
    /// </summary>
    Task<DoctorPartnerLedgerEntryResponse> AppendEntryAsync(
        DoctorPartnerLedgerEntryRequest request, CancellationToken cancellationToken);

    /// <summary>Returns the doctor-partner's full statement over the optional date window.</summary>
    Task<DoctorPartnerStatementResponse> GetStatementAsync(
        Guid doctorPartnerId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}
