using FluentValidation;
using VetSystem.Application.Vaccinations.Contracts;

namespace VetSystem.Application.Vaccinations.Validators;

public sealed class VaccinationCreateRequestValidator : AbstractValidator<VaccinationCreateRequest>
{
    public VaccinationCreateRequestValidator()
    {
        RuleFor(r => r.VaccineType).NotEmpty().MaximumLength(128);
        RuleFor(r => r.DateGiven).NotEqual(default(DateOnly)).WithMessage("DateGiven is required.");

        RuleFor(r => r)
            .Must(r => r.PetId.HasValue || r.CustomerId.HasValue)
            .WithMessage("A vaccination must target a pet or a customer (farm group).")
            .WithName("recipient");

        RuleFor(r => r.NextDueDate!.Value)
            .GreaterThanOrEqualTo(r => r.DateGiven)
            .WithMessage("NextDueDate must be on or after DateGiven.")
            .When(r => r.NextDueDate.HasValue);
    }
}

public sealed class VaccinationPatchRequestValidator : AbstractValidator<VaccinationPatchRequest>
{
    public VaccinationPatchRequestValidator()
    {
        RuleFor(r => r.VaccineType!).NotEmpty().MaximumLength(128).When(r => r.VaccineType is not null);
        RuleFor(r => r.DateGiven!.Value).NotEqual(default(DateOnly)).When(r => r.DateGiven.HasValue);
    }
}
