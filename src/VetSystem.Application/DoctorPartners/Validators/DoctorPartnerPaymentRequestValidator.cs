using FluentValidation;
using VetSystem.Application.DoctorPartners.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.DoctorPartners.Validators;

public sealed class DoctorPartnerPaymentRequestValidator : AbstractValidator<DoctorPartnerPaymentRequest>
{
    public DoctorPartnerPaymentRequestValidator()
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
