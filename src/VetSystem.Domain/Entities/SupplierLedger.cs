using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M19 (SCHEMA §4) — a supplier's running account, mirroring the customer <see cref="Ledger"/> but kept
/// a <b>separate</b> table so the settlement-lock / doctor-entitlement code on the customer/farm
/// polymorphic ledger is untouched. <see cref="Balance"/> = what the clinic owes the supplier (positive
/// = outstanding payable); it is derived from <see cref="SupplierLedgerEntry"/> rows only and never
/// written from a CRUD path. <see cref="Status"/> reuses <see cref="LedgerStatus"/> semantics
/// (<c>open</c> = settled, <c>has_debt</c> = the clinic owes, <c>closed</c>).
/// </summary>
public sealed class SupplierLedger : Entity
{
    public Guid SupplierId { get; set; }

    public decimal Balance { get; set; }

    public string Status { get; set; } = LedgerStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }
}
