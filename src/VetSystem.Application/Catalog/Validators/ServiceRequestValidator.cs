using FluentValidation;
using VetSystem.Application.Catalog.Contracts;

namespace VetSystem.Application.Catalog.Validators;

public sealed class ServiceRequestValidator : AbstractValidator<ServiceRequest>
{
    public ServiceRequestValidator()
    {
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(256);
        RuleFor(r => r.NameLatin).MaximumLength(256);
        RuleFor(r => r.Category).MaximumLength(32);
        RuleFor(r => r.DefaultPrice).GreaterThanOrEqualTo(0).WithMessage("DefaultPrice must be ≥ 0.");
    }
}

public sealed class ServicePatchRequestValidator : AbstractValidator<ServicePatchRequest>
{
    public ServicePatchRequestValidator()
    {
        RuleFor(r => r.NameAr!).NotEmpty().MaximumLength(256).When(r => r.NameAr is not null);
        RuleFor(r => r.NameLatin).MaximumLength(256);
        RuleFor(r => r.Category).MaximumLength(32);
        RuleFor(r => r.DefaultPrice!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DefaultPrice must be ≥ 0.")
            .When(r => r.DefaultPrice.HasValue);
    }
}
