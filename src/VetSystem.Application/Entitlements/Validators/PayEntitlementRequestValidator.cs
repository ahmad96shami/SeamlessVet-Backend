using FluentValidation;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Entitlements.Validators;

/// <summary>
/// M9 task 15 — the only client-supplied entitlement input is the disbursement method on pay
/// (computed amounts, percentages, and ceilings are derived server-side from batch config + invoices,
/// so they are never validated at this boundary). The method must be a known payment method.
/// </summary>
public sealed class PayEntitlementRequestValidator : AbstractValidator<PayEntitlementRequest>
{
    public PayEntitlementRequestValidator()
    {
        RuleFor(r => r.Method)
            .NotEmpty()
            .Must(PaymentMethod.All.Contains)
            .WithMessage($"method must be one of: {string.Join(", ", PaymentMethod.All)}.");
    }
}
