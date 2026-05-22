using FluentValidation;
using VetSystem.Application.Pets.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Pets.Validators;

public sealed class PetRequestValidator : AbstractValidator<PetRequest>
{
    public PetRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Species).MaximumLength(64);
        RuleFor(r => r.Breed).MaximumLength(128);
        RuleFor(r => r.Sex!)
            .Must(s => PetSex.All.Contains(s))
            .WithMessage($"Sex must be one of: {string.Join(", ", PetSex.All)}.")
            .When(r => r.Sex is not null);
        RuleFor(r => r.WeightLatest!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("WeightLatest must be ≥ 0.")
            .When(r => r.WeightLatest.HasValue);
        RuleFor(r => r.MicrochipNo).MaximumLength(64);
    }
}

public sealed class PetPatchRequestValidator : AbstractValidator<PetPatchRequest>
{
    public PetPatchRequestValidator()
    {
        RuleFor(r => r.Name!).NotEmpty().MaximumLength(128).When(r => r.Name is not null);
        RuleFor(r => r.Species).MaximumLength(64);
        RuleFor(r => r.Breed).MaximumLength(128);
        RuleFor(r => r.Sex!)
            .Must(s => PetSex.All.Contains(s))
            .WithMessage($"Sex must be one of: {string.Join(", ", PetSex.All)}.")
            .When(r => r.Sex is not null);
        RuleFor(r => r.WeightLatest!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("WeightLatest must be ≥ 0.")
            .When(r => r.WeightLatest.HasValue);
        RuleFor(r => r.MicrochipNo).MaximumLength(64);
    }
}

public sealed class PetTransferRequestValidator : AbstractValidator<PetTransferRequest>
{
    public PetTransferRequestValidator()
    {
        RuleFor(r => r.TargetCustomerId).NotEmpty();
    }
}
