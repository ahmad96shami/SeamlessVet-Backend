using FluentValidation;
using VetSystem.Application.Employees.Contracts;

namespace VetSystem.Application.Employees.Validators;

public sealed class EmployeeRequestValidator : AbstractValidator<EmployeeRequest>
{
    public EmployeeRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.JobTitle).MaximumLength(120);
        RuleFor(r => r.MonthlySalary).GreaterThanOrEqualTo(0m);
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}

public sealed class EmployeePatchRequestValidator : AbstractValidator<EmployeePatchRequest>
{
    public EmployeePatchRequestValidator()
    {
        RuleFor(r => r.FullName).MaximumLength(200).When(r => r.FullName is not null);
        RuleFor(r => r.JobTitle).MaximumLength(120);
        RuleFor(r => r.MonthlySalary).GreaterThanOrEqualTo(0m).When(r => r.MonthlySalary.HasValue);
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}
