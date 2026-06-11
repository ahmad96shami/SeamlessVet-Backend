using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M31 (SCHEMA §4) — an employee's running HR account, the AP mirror of <see cref="SupplierLedger"/> /
/// <see cref="DoctorPartnerLedger"/> kept a <b>separate</b> table so the customer/farm polymorphic ledger
/// code is untouched. <see cref="Balance"/> = what the clinic owes the employee (positive = accrued but
/// unpaid salary); a loan/advance drives it negative (the employee owes the clinic). It is derived from
/// <see cref="EmployeeLedgerEntry"/> rows only and never written from a CRUD path: a salary accrual or a
/// loan repayment credits it, a salary payment or a loan debits it. <see cref="Status"/> reuses
/// <see cref="LedgerStatus"/> semantics (<c>open</c> = settled, <c>has_debt</c> = the clinic owes,
/// <c>closed</c>).
/// </summary>
public sealed class EmployeeLedger : Entity
{
    public Guid EmployeeId { get; set; }

    public decimal Balance { get; set; }

    public string Status { get; set; } = LedgerStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }
}
