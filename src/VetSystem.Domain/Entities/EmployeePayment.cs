using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M31 (SCHEMA §4) — a cash event between the clinic and an employee (the HR mirror of
/// <see cref="SupplierPayment"/> / <see cref="DoctorPartnerPayment"/>). <see cref="Kind"/> selects the
/// ledger effect: a <c>salary_payment</c> posts a negative entry that reduces the payable; a <c>loan</c>
/// posts a negative entry (an advance, driving the balance negative); a <c>loan_repayment</c> posts a
/// positive entry. A <c>salary_payment</c> may carry <see cref="LoanRepaymentAmount"/> > 0 to repay a loan
/// out of that salary — the <b>future-salary-deduction pairing</b>: the full salary
/// (<see cref="Amount"/>) is posted as a salary_payment (−) and the deducted portion as a loan_repayment
/// (+), so the net cash handed over is <c>Amount − LoanRepaymentAmount</c>. Append-only and idempotent per
/// environment so a retried payment never double-posts. <see cref="Method"/> is one of cash / card /
/// bank_transfer / cheque (never <c>credit</c> — credit is a customer-AR concept).
/// </summary>
public sealed class EmployeePayment : Entity
{
    public Guid EmployeeId { get; set; }

    public string Kind { get; set; } = EmployeePaymentKind.SalaryPayment;

    public decimal Amount { get; set; }

    /// <summary>
    /// On a <c>salary_payment</c>, the portion of the salary withheld to repay an outstanding loan
    /// (posts a paired <c>loan_repayment</c> ledger entry). Null/0 for a plain salary payment, a loan, or
    /// a direct loan repayment.
    /// </summary>
    public decimal? LoanRepaymentAmount { get; set; }

    public string Method { get; set; } = PaymentMethod.Cash;

    public Guid PaidBy { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    public string? Notes { get; set; }

    public string? ChequeNumber { get; set; }

    public string? ChequeBank { get; set; }

    public DateOnly? ChequeDueDate { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class EmployeePaymentKind
{
    /// <summary>Salary paid to the employee (optionally with a loan deduction).</summary>
    public const string SalaryPayment = "salary_payment";

    /// <summary>An advance/loan given to the employee.</summary>
    public const string Loan = "loan";

    /// <summary>A direct cash loan repayment by the employee.</summary>
    public const string LoanRepayment = "loan_repayment";

    public static readonly IReadOnlyCollection<string> All = [SalaryPayment, Loan, LoanRepayment];
}
