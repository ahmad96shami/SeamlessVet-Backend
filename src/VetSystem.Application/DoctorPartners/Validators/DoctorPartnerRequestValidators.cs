using FluentValidation;
using VetSystem.Application.DoctorPartners.Contracts;

namespace VetSystem.Application.DoctorPartners.Validators;

public sealed class DoctorPartnerRequestValidator : AbstractValidator<DoctorPartnerRequest>
{
    public DoctorPartnerRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}

public sealed class DoctorPartnerPatchRequestValidator : AbstractValidator<DoctorPartnerPatchRequest>
{
    public DoctorPartnerPatchRequestValidator()
    {
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}
