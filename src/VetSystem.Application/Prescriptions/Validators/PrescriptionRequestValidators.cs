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
    }
}

public sealed class PrescriptionPatchRequestValidator : AbstractValidator<PrescriptionPatchRequest>
{
    public PrescriptionPatchRequestValidator()
    {
        RuleFor(r => r.Dosage).MaximumLength(128);
        RuleFor(r => r.Frequency).MaximumLength(128);
        RuleFor(r => r.Duration).MaximumLength(128);
    }
}
