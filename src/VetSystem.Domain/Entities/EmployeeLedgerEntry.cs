using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M31 (SCHEMA §4) — append-only history of an employee HR account, mirroring
/// <see cref="SupplierLedgerEntry"/> / <see cref="DoctorPartnerLedgerEntry"/>. UPDATE/DELETE is never
/// accepted; corrections are new <c>adjustment</c> rows. <see cref="Amount"/> is signed by
/// <see cref="EmployeeLedgerEntryType"/>: <c>salary_accrual</c> (+) and <c>loan_repayment</c> (+) increase
/// the payable, <c>salary_payment</c> (−) and <c>loan</c> (−) reduce it (a loan drives the balance
/// negative). <see cref="BalanceAfter"/> stores the running balance immediately after this entry applied.
/// A salary/loan event carries its source <see cref="EmployeePaymentId"/>; an accrual (posted by the
/// monthly job) and a manual adjustment have none.
/// </summary>
public sealed class EmployeeLedgerEntry : Entity
{
    public Guid EmployeeLedgerId { get; set; }

    public string EntryType { get; set; } = EmployeeLedgerEntryType.Adjustment;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public Guid? EmployeePaymentId { get; set; }

    public string? Description { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class EmployeeLedgerEntryType
{
    /// <summary>Monthly salary accrued by the job — increases the payable.</summary>
    public const string SalaryAccrual = "salary_accrual";

    /// <summary>Salary paid out to the employee — reduces the payable.</summary>
    public const string SalaryPayment = "salary_payment";

    /// <summary>An advance/loan given to the employee — reduces the payable (drives it negative).</summary>
    public const string Loan = "loan";

    /// <summary>The employee repaying a loan (direct cash or a future-salary deduction) — increases the payable.</summary>
    public const string LoanRepayment = "loan_repayment";

    /// <summary>Manual correction — signed either way.</summary>
    public const string Adjustment = "adjustment";

    public static readonly IReadOnlyCollection<string> All =
        [SalaryAccrual, SalaryPayment, Loan, LoanRepayment, Adjustment];
}
