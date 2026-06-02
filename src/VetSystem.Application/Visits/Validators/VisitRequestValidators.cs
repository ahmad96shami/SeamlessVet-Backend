using FluentValidation;
using VetSystem.Application.Visits.Contracts;
using VetSystem.Domain.Entities;
using DomainSeverity = VetSystem.Domain.Entities.Severity;

namespace VetSystem.Application.Visits.Validators;

/// <summary>
/// Static-shape validation. The <c>visit_number</c> format/prefix/uniqueness rules live in
/// <c>IVisitNumberValidator</c> (DB-aware) and run in the service, so they are not duplicated here.
/// </summary>
public sealed class VisitCreateRequestValidator : AbstractValidator<VisitCreateRequest>
{
    public VisitCreateRequestValidator()
    {
        RuleFor(r => r.VisitType)
            .Must(VisitType.All.Contains)
            .WithMessage($"VisitType must be one of: {string.Join(", ", VisitType.All)}.");

        RuleFor(r => r.CustomerId).NotEmpty();
        RuleFor(r => r.DoctorId).NotEmpty();

        RuleFor(r => r.Status!)
            .Must(VisitStatus.Creatable.Contains)
            .WithMessage($"A visit can only be created with status: {string.Join(", ", VisitStatus.Creatable)}.")
            .When(r => r.Status is not null);

        RuleFor(r => r.Severity!)
            .Must(DomainSeverity.All.Contains)
            .WithMessage($"Severity must be one of: {string.Join(", ", DomainSeverity.All)}.")
            .When(r => r.Severity is not null);

        RuleFor(r => r.Temperature!.Value).GreaterThanOrEqualTo(0).When(r => r.Temperature.HasValue);
        RuleFor(r => r.HeartRate!.Value).GreaterThanOrEqualTo(0).When(r => r.HeartRate.HasValue);
        RuleFor(r => r.RespiratoryRate!.Value).GreaterThanOrEqualTo(0).When(r => r.RespiratoryRate.HasValue);
        RuleFor(r => r.Weight!.Value).GreaterThanOrEqualTo(0).When(r => r.Weight.HasValue);
        RuleFor(r => r.ExamFeeApplied!.Value).GreaterThanOrEqualTo(0).When(r => r.ExamFeeApplied.HasValue);
        RuleFor(r => r.CheckupFeeApplied!.Value).GreaterThanOrEqualTo(0).When(r => r.CheckupFeeApplied.HasValue);
    }
}

public sealed class VisitPatchRequestValidator : AbstractValidator<VisitPatchRequest>
{
    public VisitPatchRequestValidator()
    {
        RuleFor(r => r.Status!)
            .Must(VisitStatus.All.Contains)
            .WithMessage($"Status must be one of: {string.Join(", ", VisitStatus.All)}.")
            .When(r => r.Status is not null);

        RuleFor(r => r.Severity!)
            .Must(DomainSeverity.All.Contains)
            .WithMessage($"Severity must be one of: {string.Join(", ", DomainSeverity.All)}.")
            .When(r => r.Severity is not null);

        RuleFor(r => r.Temperature!.Value).GreaterThanOrEqualTo(0).When(r => r.Temperature.HasValue);
        RuleFor(r => r.HeartRate!.Value).GreaterThanOrEqualTo(0).When(r => r.HeartRate.HasValue);
        RuleFor(r => r.RespiratoryRate!.Value).GreaterThanOrEqualTo(0).When(r => r.RespiratoryRate.HasValue);
        RuleFor(r => r.Weight!.Value).GreaterThanOrEqualTo(0).When(r => r.Weight.HasValue);
        RuleFor(r => r.ExamFeeApplied!.Value).GreaterThanOrEqualTo(0).When(r => r.ExamFeeApplied.HasValue);
        RuleFor(r => r.CheckupFeeApplied!.Value).GreaterThanOrEqualTo(0).When(r => r.CheckupFeeApplied.HasValue);
    }
}
