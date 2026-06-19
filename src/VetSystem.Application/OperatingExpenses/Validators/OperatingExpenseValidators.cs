using FluentValidation;
using VetSystem.Application.OperatingExpenses.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.OperatingExpenses.Validators;

public sealed class CreateOperatingExpenseRequestValidator : AbstractValidator<CreateOperatingExpenseRequest>
{
    public CreateOperatingExpenseRequestValidator()
    {
        RuleFor(x => x.Category).NotEmpty()
            .Must(OperatingExpenseCategory.All.Contains)
            .WithMessage("Unknown expense category.");
        RuleFor(x => x.Amount).GreaterThan(0m);
        RuleFor(x => x.IncurredOn).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1024);
    }
}

public sealed class UpdateOperatingExpenseRequestValidator : AbstractValidator<UpdateOperatingExpenseRequest>
{
    public UpdateOperatingExpenseRequestValidator()
    {
        RuleFor(x => x.Category)
            .Must(c => c is null || OperatingExpenseCategory.All.Contains(c))
            .WithMessage("Unknown expense category.");
        RuleFor(x => x.Amount).GreaterThan(0m).When(x => x.Amount.HasValue);
        RuleFor(x => x.Note).MaximumLength(1024);
    }
}
