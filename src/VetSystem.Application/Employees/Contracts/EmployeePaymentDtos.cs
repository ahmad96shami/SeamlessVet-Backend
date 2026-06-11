namespace VetSystem.Application.Employees.Contracts;

/// <summary>
/// Employee payment issuance (M31). The employee is taken from the route, not the body. <c>Kind</c> is
/// one of salary_payment / loan / loan_repayment and selects the ledger effect (see
/// <see cref="VetSystem.Domain.Entities.EmployeePaymentKind"/>). On a <c>salary_payment</c>,
/// <c>LoanRepaymentAmount</c> > 0 repays a loan out of that salary (the future-salary-deduction pairing):
/// the full salary (<c>Amount</c>) posts as a salary_payment and the deducted portion as a loan_repayment,
/// so the net cash handed over is <c>Amount − LoanRepaymentAmount</c>. <c>Method</c> is one of cash / card
/// / bank_transfer / cheque (never credit). Cheque metadata is optional and stored when <c>Method</c> is
/// <c>cheque</c>; a cheque settles immediately.
/// </summary>
public sealed record EmployeePaymentRequest(
    Guid? Id,
    string Kind,
    decimal Amount,
    decimal? LoanRepaymentAmount,
    string Method,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    string IdempotencyKey);

public sealed record EmployeePaymentResponse(
    Guid Id,
    Guid EmployeeId,
    string Kind,
    decimal Amount,
    decimal? LoanRepaymentAmount,
    string Method,
    Guid PaidBy,
    DateTimeOffset PaidAt,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    DateTimeOffset CreatedAt);
