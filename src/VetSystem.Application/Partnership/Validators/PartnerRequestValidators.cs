using FluentValidation;

namespace VetSystem.Application.Partnership.Validators;

/// <summary>Static-shape validation for partners. Existence of <c>UserId</c> is checked in the service.</summary>
public sealed class PartnerCreateRequestValidator : AbstractValidator<PartnerCreateRequest>
{
    public PartnerCreateRequestValidator()
    {
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(256);
    }
}

public sealed class PartnerPatchRequestValidator : AbstractValidator<PartnerPatchRequest>
{
    public PartnerPatchRequestValidator()
    {
        RuleFor(r => r.DisplayName!).NotEmpty().MaximumLength(256).When(r => r.DisplayName is not null);
    }
}
