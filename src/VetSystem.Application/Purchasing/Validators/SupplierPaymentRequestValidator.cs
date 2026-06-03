using FluentValidation;
using VetSystem.Application.Purchasing.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Purchasing.Validators;

public sealed class SupplierPaymentRequestValidator : AbstractValidator<SupplierPaymentRequest>
{
    public SupplierPaymentRequestValidator()
    {
        RuleFor(r => r.Amount).GreaterThan(0m);
        RuleFor(r => r.Method)
            .Must(PaymentMethod.Supplier.Contains)
            .WithMessage($"method must be one of: {string.Join(", ", PaymentMethod.Supplier)}.");
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.ChequeNumber).MaximumLength(64);
        RuleFor(r => r.ChequeBank).MaximumLength(128);
    }
}
