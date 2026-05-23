using FluentValidation;
using VetSystem.Application.Appointments.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Appointments.Validators;

/// <summary>
/// Static-shape validation. Conflict detection (DB-aware) and the status state machine run in the
/// service, so they are not duplicated here. <c>duration_min</c> is bounded by
/// <see cref="AppointmentSchedule.MaxDurationMin"/> — this is what keeps the conflict-detection
/// look-back window finite and index-bounded.
/// </summary>
public sealed class AppointmentCreateRequestValidator : AbstractValidator<AppointmentCreateRequest>
{
    public AppointmentCreateRequestValidator()
    {
        RuleFor(r => r.ScheduledAt).NotEmpty();

        RuleFor(r => r.DurationMin!.Value)
            .InclusiveBetween(1, AppointmentSchedule.MaxDurationMin)
            .When(r => r.DurationMin.HasValue)
            .WithMessage($"DurationMin must be between 1 and {AppointmentSchedule.MaxDurationMin} minutes.");

        RuleFor(r => r.Status!)
            .Must(AppointmentStatus.Creatable.Contains)
            .WithMessage($"An appointment can only be created with status: {string.Join(", ", AppointmentStatus.Creatable)}.")
            .When(r => r.Status is not null);
    }
}

public sealed class AppointmentPatchRequestValidator : AbstractValidator<AppointmentPatchRequest>
{
    public AppointmentPatchRequestValidator()
    {
        RuleFor(r => r.DurationMin!.Value)
            .InclusiveBetween(1, AppointmentSchedule.MaxDurationMin)
            .When(r => r.DurationMin.HasValue)
            .WithMessage($"DurationMin must be between 1 and {AppointmentSchedule.MaxDurationMin} minutes.");

        // Terminal transitions go through /attend, /cancel, /no-show — PATCH may only set the
        // non-terminal states; the service additionally enforces the allowed transition.
        RuleFor(r => r.Status!)
            .Must(AppointmentStatus.Creatable.Contains)
            .WithMessage($"PATCH may only set status to: {string.Join(", ", AppointmentStatus.Creatable)}. "
                + "Use /attend, /cancel, or /no-show to close an appointment.")
            .When(r => r.Status is not null);
    }
}
