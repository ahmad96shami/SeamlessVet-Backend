using VetSystem.Application.EmployeeLedgers.Contracts;

namespace VetSystem.Application.EmployeeLedgers;

/// <summary>
/// M31 (SCHEMA §4) — the only request-scoped path that creates <c>employee_ledger_entries</c>.
/// INSERT-only; corrections are new <c>adjustment</c> rows, never updates or deletes. The implementation
/// atomically computes <c>balance_after</c> from the current balance, then bumps
/// <see cref="VetSystem.Domain.Entities.EmployeeLedger.Balance"/> and transitions
/// <see cref="VetSystem.Domain.Entities.EmployeeLedger.Status"/> (open ⇄ has_debt) in the same
/// SaveChanges. Mirrors <c>ISupplierLedgerService</c> / <c>IDoctorPartnerLedgerService</c> on the HR side.
/// Used by the salary-payment / loan flows (the monthly accrual job appends with an explicit environment,
/// outside this request-scoped path, because it has no HTTP principal).
/// </summary>
public interface IEmployeeLedgerService
{
    /// <summary>
    /// Appends an employee-ledger entry. Idempotency is enforced via the
    /// <c>ux_employee_ledger_entries_env_idempotency</c> unique index; a duplicate idempotency key
    /// returns the original row instead of failing.
    /// </summary>
    Task<EmployeeLedgerEntryResponse> AppendEntryAsync(
        EmployeeLedgerEntryRequest request, CancellationToken cancellationToken);

    /// <summary>Returns the employee's full statement over the optional date window.</summary>
    Task<EmployeeStatementResponse> GetStatementAsync(
        Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}
