using FluentValidation;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Contracts.Validators;

/// <summary>
/// Static-shape validation. Existence checks (customer, doctor), the activation-permission gate, and
/// the status state machine are DB-/auth-aware and run in the service, so they aren't duplicated here.
/// </summary>
public sealed class ContractCreateRequestValidator : AbstractValidator<ContractCreateRequest>
{
    public ContractCreateRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty();

        RuleFor(r => r.Status!)
            .Must(ContractStatus.Creatable.Contains)
            .WithMessage($"A contract can only be created with status: {string.Join(", ", ContractStatus.Creatable)}.")
            .When(r => r.Status is not null);

        RuleFor(r => r.PeriodEnd!.Value)
            .GreaterThanOrEqualTo(r => r.PeriodStart)
            .WithMessage("period_end must be on or after period_start.")
            .When(r => r.PeriodEnd.HasValue);

        RuleFor(r => r.TotalPrice!.Value).GreaterThanOrEqualTo(0m).When(r => r.TotalPrice.HasValue);
        RuleFor(r => r.ExpectedVisitCount!.Value).GreaterThanOrEqualTo(0).When(r => r.ExpectedVisitCount.HasValue);
        RuleFor(r => r.AnimalCount!.Value).GreaterThanOrEqualTo(0).When(r => r.AnimalCount.HasValue);
    }
}

public sealed class ContractPatchRequestValidator : AbstractValidator<ContractPatchRequest>
{
    public ContractPatchRequestValidator()
    {
        RuleFor(r => r.PeriodEnd!.Value)
            .GreaterThanOrEqualTo(r => r.PeriodStart!.Value)
            .WithMessage("period_end must be on or after period_start.")
            .When(r => r.PeriodEnd.HasValue && r.PeriodStart.HasValue);

        RuleFor(r => r.TotalPrice!.Value).GreaterThanOrEqualTo(0m).When(r => r.TotalPrice.HasValue);
        RuleFor(r => r.ExpectedVisitCount!.Value).GreaterThanOrEqualTo(0).When(r => r.ExpectedVisitCount.HasValue);
        RuleFor(r => r.AnimalCount!.Value).GreaterThanOrEqualTo(0).When(r => r.AnimalCount.HasValue);
    }
}
