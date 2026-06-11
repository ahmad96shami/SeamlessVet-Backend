using FluentValidation;
using VetSystem.Application.Purchasing.Contracts;

namespace VetSystem.Application.Purchasing.Validators;

internal sealed class PurchaseInvoiceLineRequestValidator : AbstractValidator<PurchaseInvoiceLineRequest>
{
    public PurchaseInvoiceLineRequestValidator()
    {
        RuleFor(l => l.ProductId).NotEmpty();
        RuleFor(l => l.Quantity).GreaterThan(0m);
        RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0m);
        RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0m);
        RuleFor(l => l.LotNumber).MaximumLength(64);
    }
}

public sealed class PurchaseInvoiceRequestValidator : AbstractValidator<PurchaseInvoiceRequest>
{
    public PurchaseInvoiceRequestValidator()
    {
        RuleFor(r => r.SupplierId).NotEmpty();
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.DiscountAmount).GreaterThanOrEqualTo(0m);
        RuleFor(r => r.TaxAmount!.Value).GreaterThanOrEqualTo(0m).When(r => r.TaxAmount.HasValue);
        RuleFor(r => r.Items).NotEmpty().WithMessage("A purchase invoice needs at least one line item.");
        RuleForEach(r => r.Items).SetValidator(new PurchaseInvoiceLineRequestValidator());
    }
}
