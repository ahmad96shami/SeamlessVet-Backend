using FluentValidation;
using VetSystem.Application.Prescriptions.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Prescriptions.Validators;

public sealed class PrescriptionCreateRequestValidator : AbstractValidator<PrescriptionCreateRequest>
{
    public PrescriptionCreateRequestValidator()
    {
        RuleFor(r => r.VisitId).NotEmpty();
        RuleFor(r => r.ProductId).NotEmpty();

        RuleFor(r => r.DispenseType)
            .Must(DispenseType.All.Contains)
            .WithMessage($"DispenseType must be one of: {string.Join(", ", DispenseType.All)}.");

        // Both paths act on a concrete amount: administered deducts it, dispensed invoices it.
        RuleFor(r => r.Quantity)
            .NotNull().WithMessage("Quantity is required.")
            .Must(q => q!.Value > 0).WithMessage("Quantity must be greater than 0.");

        // M18 reminder schedule. A recurrence needs at minimum a step and a start.
        When(r => r.ReminderEnabled, () =>
        {
            RuleFor(r => r.IntervalMinutes)
                .NotNull().WithMessage("IntervalMinutes is required when reminders are enabled.");
            RuleFor(r => r.StartAt)
                .NotNull().WithMessage("StartAt is required when reminders are enabled.");
        });

        RuleFor(r => r.IntervalMinutes!.Value).GreaterThan(0)
            .WithMessage("IntervalMinutes must be greater than 0.").When(r => r.IntervalMinutes.HasValue);
        RuleFor(r => r.LeadMinutes!.Value).GreaterThanOrEqualTo(0)
            .WithMessage("LeadMinutes must be ≥ 0.").When(r => r.LeadMinutes.HasValue);
        RuleFor(r => r.DosesCount!.Value).GreaterThan(0)
            .WithMessage("DosesCount must be greater than 0.").When(r => r.DosesCount.HasValue);
        RuleFor(r => r)
            .Must(r => r.EndAt!.Value >= r.StartAt!.Value)
            .WithMessage("EndAt must be on or after StartAt.").WithName("endAt")
            .When(r => r.StartAt.HasValue && r.EndAt.HasValue);
    }
}

public sealed class PrescriptionPatchRequestValidator : AbstractValidator<PrescriptionPatchRequest>
{
    public PrescriptionPatchRequestValidator()
    {
        RuleFor(r => r.Dosage).MaximumLength(128);
        RuleFor(r => r.Frequency).MaximumLength(128);
        RuleFor(r => r.Duration).MaximumLength(128);

        // M18 — schedule field sanity when present (the "required-when-enabled" pairing is only checked
        // on create; a patch may rely on values already on the row, and the job skips half-set schedules).
        RuleFor(r => r.IntervalMinutes!.Value).GreaterThan(0)
            .WithMessage("IntervalMinutes must be greater than 0.").When(r => r.IntervalMinutes.HasValue);
        RuleFor(r => r.LeadMinutes!.Value).GreaterThanOrEqualTo(0)
            .WithMessage("LeadMinutes must be ≥ 0.").When(r => r.LeadMinutes.HasValue);
        RuleFor(r => r.DosesCount!.Value).GreaterThan(0)
            .WithMessage("DosesCount must be greater than 0.").When(r => r.DosesCount.HasValue);
        RuleFor(r => r)
            .Must(r => r.EndAt!.Value >= r.StartAt!.Value)
            .WithMessage("EndAt must be on or after StartAt.").WithName("endAt")
            .When(r => r.StartAt.HasValue && r.EndAt.HasValue);
    }
}
