using FluentValidation;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Contracts.Validators;

/// <summary>
/// Static-shape validation for batches. Existence checks (customer, doctor, contract) run in the
/// service. Percent bounds and fee-model / entitlement-system / status enums are checked here.
/// </summary>
public sealed class BatchCreateRequestValidator : AbstractValidator<BatchCreateRequest>
{
    public BatchCreateRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty();
        RuleFor(r => r.ResponsibleDoctorId).NotEmpty();
        RuleFor(r => r.AnimalCount).GreaterThanOrEqualTo(0);

        RuleFor(r => r.SupervisionFeeModel)
            .Must(FeeModel.All.Contains)
            .WithMessage($"supervision_fee_model must be one of: {string.Join(", ", FeeModel.All)}.");
        RuleFor(r => r.SupervisionFeeValue).GreaterThanOrEqualTo(0m);

        RuleFor(r => r.EntitlementSystem!)
            .Must(EntitlementSystem.All.Contains)
            .WithMessage($"entitlement_system must be one of: {string.Join(", ", EntitlementSystem.All)}.")
            .When(r => r.EntitlementSystem is not null);

        RuleFor(r => r.DoctorSharePercent!.Value).InclusiveBetween(0m, 100m).When(r => r.DoctorSharePercent.HasValue);
        RuleFor(r => r.DoctorShareCeiling!.Value).GreaterThanOrEqualTo(0m).When(r => r.DoctorShareCeiling.HasValue);

        RuleFor(r => r.Status!)
            .Must(BatchStatus.All.Contains)
            .WithMessage($"status must be one of: {string.Join(", ", BatchStatus.All)}.")
            .When(r => r.Status is not null);

        RuleFor(r => r.EndDate!.Value)
            .GreaterThanOrEqualTo(r => r.StartDate)
            .WithMessage("end_date must be on or after start_date.")
            .When(r => r.EndDate.HasValue);
    }
}

public sealed class BatchPatchRequestValidator : AbstractValidator<BatchPatchRequest>
{
    public BatchPatchRequestValidator()
    {
        RuleFor(r => r.AnimalCount!.Value).GreaterThanOrEqualTo(0).When(r => r.AnimalCount.HasValue);

        RuleFor(r => r.SupervisionFeeModel!)
            .Must(FeeModel.All.Contains)
            .WithMessage($"supervision_fee_model must be one of: {string.Join(", ", FeeModel.All)}.")
            .When(r => r.SupervisionFeeModel is not null);
        RuleFor(r => r.SupervisionFeeValue!.Value).GreaterThanOrEqualTo(0m).When(r => r.SupervisionFeeValue.HasValue);

        RuleFor(r => r.EntitlementSystem!)
            .Must(EntitlementSystem.All.Contains)
            .WithMessage($"entitlement_system must be one of: {string.Join(", ", EntitlementSystem.All)}.")
            .When(r => r.EntitlementSystem is not null);

        RuleFor(r => r.DoctorSharePercent!.Value).InclusiveBetween(0m, 100m).When(r => r.DoctorSharePercent.HasValue);
        RuleFor(r => r.DoctorShareCeiling!.Value).GreaterThanOrEqualTo(0m).When(r => r.DoctorShareCeiling.HasValue);

        RuleFor(r => r.Status!)
            .Must(BatchStatus.All.Contains)
            .WithMessage($"status must be one of: {string.Join(", ", BatchStatus.All)}.")
            .When(r => r.Status is not null);

        RuleFor(r => r.EndDate!.Value)
            .GreaterThanOrEqualTo(r => r.StartDate!.Value)
            .WithMessage("end_date must be on or after start_date.")
            .When(r => r.EndDate.HasValue && r.StartDate.HasValue);
    }
}
