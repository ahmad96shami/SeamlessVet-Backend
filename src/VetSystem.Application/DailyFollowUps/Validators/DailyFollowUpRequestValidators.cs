using FluentValidation;
using VetSystem.Application.DailyFollowUps.Contracts;

namespace VetSystem.Application.DailyFollowUps.Validators;

public sealed class DailyFollowUpCreateRequestValidator : AbstractValidator<DailyFollowUpCreateRequest>
{
    public DailyFollowUpCreateRequestValidator()
    {
        RuleFor(r => r.VisitId).NotEmpty();
        RuleFor(r => r.EntryDate).NotEqual(default(DateOnly)).WithMessage("EntryDate is required.");
        RuleFor(r => r.Temperature!.Value).GreaterThanOrEqualTo(0).When(r => r.Temperature.HasValue);
        RuleFor(r => r.HeartRate!.Value).GreaterThanOrEqualTo(0).When(r => r.HeartRate.HasValue);
        RuleFor(r => r.RespiratoryRate!.Value).GreaterThanOrEqualTo(0).When(r => r.RespiratoryRate.HasValue);
    }
}

public sealed class DailyFollowUpPatchRequestValidator : AbstractValidator<DailyFollowUpPatchRequest>
{
    public DailyFollowUpPatchRequestValidator()
    {
        RuleFor(r => r.EntryDate!.Value).NotEqual(default(DateOnly)).When(r => r.EntryDate.HasValue);
        RuleFor(r => r.Temperature!.Value).GreaterThanOrEqualTo(0).When(r => r.Temperature.HasValue);
        RuleFor(r => r.HeartRate!.Value).GreaterThanOrEqualTo(0).When(r => r.HeartRate.HasValue);
        RuleFor(r => r.RespiratoryRate!.Value).GreaterThanOrEqualTo(0).When(r => r.RespiratoryRate.HasValue);
    }
}
