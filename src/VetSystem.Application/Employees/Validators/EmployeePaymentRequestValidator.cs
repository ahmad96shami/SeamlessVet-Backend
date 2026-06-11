using FluentValidation;
using VetSystem.Application.Employees.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Employees.Validators;

public sealed class EmployeePaymentRequestValidator : AbstractValidator<EmployeePaymentRequest>
{
    public EmployeePaymentRequestValidator()
    {
        RuleFor(r => r.Amount).GreaterThan(0m);

        RuleFor(r => r.Kind)
            .Must(EmployeePaymentKind.All.Contains)
            .WithMessage($"kind must be one of: {string.Join(", ", EmployeePaymentKind.All)}.");

        RuleFor(r => r.Method)
            .Must(PaymentMethod.Supplier.Contains)
            .WithMessage($"method must be one of: {string.Join(", ", PaymentMethod.Supplier)}.");

        // A loan deduction only makes sense when paying a salary, and never exceeds the salary paid.
        RuleFor(r => r.LoanRepaymentAmount)
            .Must((req, ded) => ded is null || ded.Value == 0m || req.Kind == EmployeePaymentKind.SalaryPayment)
            .WithMessage("loanRepaymentAmount is only valid on a salary_payment.");
        RuleFor(r => r.LoanRepaymentAmount)
            .GreaterThanOrEqualTo(0m).When(r => r.LoanRepaymentAmount.HasValue);
        RuleFor(r => r.LoanRepaymentAmount)
            .Must((req, ded) => ded is null || ded.Value <= req.Amount)
            .WithMessage("loanRepaymentAmount cannot exceed the salary amount.");

        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.ChequeNumber).MaximumLength(64);
        RuleFor(r => r.ChequeBank).MaximumLength(128);
    }
}
