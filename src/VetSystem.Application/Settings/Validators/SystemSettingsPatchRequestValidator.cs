using FluentValidation;
using VetSystem.Application.Settings.Contracts;

namespace VetSystem.Application.Settings.Validators;

public sealed class SystemSettingsPatchRequestValidator : AbstractValidator<SystemSettingsPatchRequest>
{
    public SystemSettingsPatchRequestValidator()
    {
        // A center must keep a name; when the rename field is supplied it has to be non-blank and ≤ 200
        // chars (matches the platform-console provisioning bound on Environment.Name).
        RuleFor(r => r.CenterName)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("CenterName must not be empty.")
            .MaximumLength(200).WithMessage("CenterName must be ≤ 200 characters.")
            .When(r => r.CenterName is not null);

        RuleFor(r => r.DefaultExamFee!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DefaultExamFee must be ≥ 0.")
            .When(r => r.DefaultExamFee.HasValue);

        RuleFor(r => r.DefaultCheckupFee!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DefaultCheckupFee must be ≥ 0.")
            .When(r => r.DefaultCheckupFee.HasValue);

        RuleFor(r => r.NightStayRateMedical!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("NightStayRateMedical must be ≥ 0.")
            .When(r => r.NightStayRateMedical.HasValue);

        RuleFor(r => r.NightStayRateIcu!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("NightStayRateIcu must be ≥ 0.")
            .When(r => r.NightStayRateIcu.HasValue);

        RuleFor(r => r.NightStayRateHotel!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("NightStayRateHotel must be ≥ 0.")
            .When(r => r.NightStayRateHotel.HasValue);

        RuleFor(r => r.NightStayCheckoutHour!.Value)
            .InclusiveBetween(0, 23).WithMessage("NightStayCheckoutHour must be in 0–23.")
            .When(r => r.NightStayCheckoutHour.HasValue);

        RuleFor(r => r.MedicationReminderLeadMinutes!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("MedicationReminderLeadMinutes must be ≥ 0.")
            .When(r => r.MedicationReminderLeadMinutes.HasValue);

        RuleFor(r => r.VaccinationReminderLeadDays!.Value)
            .InclusiveBetween(0, 365).WithMessage("VaccinationReminderLeadDays must be in 0–365.")
            .When(r => r.VaccinationReminderLeadDays.HasValue);

        RuleFor(r => r.AppointmentReminderLeadMinutes!.Value)
            .InclusiveBetween(0, 10080).WithMessage("AppointmentReminderLeadMinutes must be in 0–10080 (≤ 7 days).")
            .When(r => r.AppointmentReminderLeadMinutes.HasValue);

        RuleFor(r => r.LowStockThresholdPct!.Value)
            .InclusiveBetween(0m, 100m).WithMessage("LowStockThresholdPct must be in 0–100.")
            .When(r => r.LowStockThresholdPct.HasValue);

        RuleFor(r => r.ExpirationWarningDays!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("ExpirationWarningDays must be ≥ 0.")
            .When(r => r.ExpirationWarningDays.HasValue);

        RuleFor(r => r.TaxRate!.Value)
            .InclusiveBetween(0m, 100m).WithMessage("TaxRate must be in 0–100.")
            .When(r => r.TaxRate.HasValue);

        RuleFor(r => r.LogoUrl).MaximumLength(2048);
    }
}
