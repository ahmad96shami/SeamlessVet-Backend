using FluentValidation;
using VetSystem.Application.Customers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Customers.Validators;

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Type)
            .NotEmpty()
            .Must(t => CustomerType.All.Contains(t))
            .WithMessage($"Type must be one of: {string.Join(", ", CustomerType.All)}.");
        RuleFor(r => r.PhonePrimary).MaximumLength(32);
        RuleFor(r => r.PhoneSecondary).MaximumLength(32);
        RuleFor(r => r.Email).MaximumLength(256);
        RuleFor(r => r.IdNumber).MaximumLength(64);
    }
}

public sealed class CustomerPatchRequestValidator : AbstractValidator<CustomerPatchRequest>
{
    public CustomerPatchRequestValidator()
    {
        RuleFor(r => r.FullName!).NotEmpty().MaximumLength(256).When(r => r.FullName is not null);
        RuleFor(r => r.Type!)
            .Must(t => CustomerType.All.Contains(t))
            .WithMessage($"Type must be one of: {string.Join(", ", CustomerType.All)}.")
            .When(r => r.Type is not null);
        RuleFor(r => r.PhonePrimary).MaximumLength(32);
        RuleFor(r => r.PhoneSecondary).MaximumLength(32);
        RuleFor(r => r.Email).MaximumLength(256);
        RuleFor(r => r.IdNumber).MaximumLength(64);
    }
}
