using FluentValidation;
using VetSystem.Application.Farms.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Farms.Validators;

public sealed class FarmRequestValidator : AbstractValidator<FarmRequest>
{
    public FarmRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Kind)
            .Must(k => FarmKind.All.Contains(k))
            .WithMessage($"Kind must be one of: {string.Join(", ", FarmKind.All)}.");
        RuleFor(r => r.AnimalType).MaximumLength(64);
        RuleFor(r => r.HeadCount!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("HeadCount must be ≥ 0.")
            .When(r => r.HeadCount.HasValue);
    }
}

public sealed class FarmPatchRequestValidator : AbstractValidator<FarmPatchRequest>
{
    public FarmPatchRequestValidator()
    {
        RuleFor(r => r.Name!).NotEmpty().MaximumLength(128).When(r => r.Name is not null);
        RuleFor(r => r.Kind!)
            .Must(k => FarmKind.All.Contains(k))
            .WithMessage($"Kind must be one of: {string.Join(", ", FarmKind.All)}.")
            .When(r => r.Kind is not null);
        RuleFor(r => r.AnimalType).MaximumLength(64);
        RuleFor(r => r.HeadCount!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("HeadCount must be ≥ 0.")
            .When(r => r.HeadCount.HasValue);
    }
}
