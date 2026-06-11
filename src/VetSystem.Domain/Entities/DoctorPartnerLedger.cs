using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M30 (SCHEMA §4) — a doctor-partner's running account, the AP mirror of the customer
/// <see cref="Ledger"/> but kept a <b>separate</b> table (like <see cref="SupplierLedger"/>) so the
/// customer/farm polymorphic ledger code is untouched. <see cref="Balance"/> = what the clinic owes the
/// doctor (positive = an outstanding entitlement payable); it is derived from
/// <see cref="DoctorPartnerLedgerEntry"/> rows only and never written from a CRUD path. A batch
/// settlement credits it (+entitlement); a payment debits it (−payment). <see cref="Status"/> reuses
/// <see cref="LedgerStatus"/> semantics (<c>open</c> = settled, <c>has_debt</c> = the clinic owes,
/// <c>closed</c>).
/// </summary>
public sealed class DoctorPartnerLedger : Entity
{
    public Guid DoctorPartnerId { get; set; }

    public decimal Balance { get; set; }

    public string Status { get; set; } = LedgerStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }
}
