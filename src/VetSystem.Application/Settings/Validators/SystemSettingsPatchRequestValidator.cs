using FluentValidation;
using VetSystem.Application.Settings.Contracts;

namespace VetSystem.Application.Settings.Validators;

public sealed class SystemSettingsPatchRequestValidator : AbstractValidator<SystemSettingsPatchRequest>
{
    public SystemSettingsPatchRequestValidator()
    {
        RuleFor(r => r.DefaultExamFee!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DefaultExamFee must be ≥ 0.")
            .When(r => r.DefaultExamFee.HasValue);

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
