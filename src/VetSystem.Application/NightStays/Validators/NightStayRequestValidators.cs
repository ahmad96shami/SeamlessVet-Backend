using FluentValidation;
using VetSystem.Application.NightStays.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.NightStays.Validators;

public sealed class NightStayCreateRequestValidator : AbstractValidator<NightStayCreateRequest>
{
    public NightStayCreateRequestValidator()
    {
        RuleFor(r => r.VisitId).NotEmpty();
        RuleFor(r => r.CareType)
            .Must(CareType.All.Contains)
            .WithMessage($"CareType must be one of: {string.Join(", ", CareType.All)}.");
        RuleFor(r => r.NightlyRate!.Value).GreaterThanOrEqualTo(0).When(r => r.NightlyRate.HasValue);
        RuleFor(r => r.ExitHour!.Value).InclusiveBetween(0, 23)
            .WithMessage("ExitHour must be in 0–23.").When(r => r.ExitHour.HasValue);
    }
}

public sealed class NightStayPatchRequestValidator : AbstractValidator<NightStayPatchRequest>
{
    public NightStayPatchRequestValidator()
    {
        RuleFor(r => r.CareType!)
            .Must(CareType.All.Contains)
            .WithMessage($"CareType must be one of: {string.Join(", ", CareType.All)}.")
            .When(r => r.CareType is not null);
        RuleFor(r => r.NightlyRate!.Value).GreaterThanOrEqualTo(0).When(r => r.NightlyRate.HasValue);
        RuleFor(r => r.ExitHour!.Value).InclusiveBetween(0, 23)
            .WithMessage("ExitHour must be in 0–23.").When(r => r.ExitHour.HasValue);
    }
}
