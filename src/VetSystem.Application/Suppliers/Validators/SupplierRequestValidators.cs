using FluentValidation;
using VetSystem.Application.Suppliers.Contracts;

namespace VetSystem.Application.Suppliers.Validators;

public sealed class SupplierRequestValidator : AbstractValidator<SupplierRequest>
{
    public SupplierRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(256);
        RuleFor(r => r.PhonePrimary).MaximumLength(32);
        RuleFor(r => r.PhoneSecondary).MaximumLength(32);
        RuleFor(r => r.Email).MaximumLength(256);
        RuleFor(r => r.TaxNumber).MaximumLength(64);
    }
}

public sealed class SupplierPatchRequestValidator : AbstractValidator<SupplierPatchRequest>
{
    public SupplierPatchRequestValidator()
    {
        RuleFor(r => r.Name!).NotEmpty().MaximumLength(256).When(r => r.Name is not null);
        RuleFor(r => r.PhonePrimary).MaximumLength(32);
        RuleFor(r => r.PhoneSecondary).MaximumLength(32);
        RuleFor(r => r.Email).MaximumLength(256);
        RuleFor(r => r.TaxNumber).MaximumLength(64);
    }
}
