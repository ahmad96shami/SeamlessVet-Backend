using FluentValidation;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Financial.Validators;

/// <summary>
/// Static-shape validation only. Catalog-price resolution, cost snapshot, payment-sum ≤ total, and
/// the ledger/inventory side effects run in the service (DB-aware), so they aren't duplicated here.
/// </summary>
internal sealed class InvoiceLineRequestValidator : AbstractValidator<InvoiceLineRequest>
{
    public InvoiceLineRequestValidator()
    {
        RuleFor(l => l)
            .Must(l => (l.ProductId is not null) ^ (l.ServiceId is not null))
            .WithMessage("Each line must reference exactly one of product_id or service_id.");

        RuleFor(l => l.Quantity).GreaterThan(0m);
        RuleFor(l => l.UnitPrice!.Value).GreaterThanOrEqualTo(0m).When(l => l.UnitPrice.HasValue);
        RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0m);
    }
}

internal sealed class PaymentRequestValidator : AbstractValidator<PaymentRequest>
{
    public PaymentRequestValidator()
    {
        RuleFor(p => p.Method)
            .Must(PaymentMethod.All.Contains)
            .WithMessage($"method must be one of: {string.Join(", ", PaymentMethod.All)}.");
        RuleFor(p => p.Amount).GreaterThan(0m);
    }
}

public sealed class PosInvoiceRequestValidator : AbstractValidator<PosInvoiceRequest>
{
    public PosInvoiceRequestValidator()
    {
        RuleFor(r => r.IdempotencyKey).NotEmpty();
        RuleFor(r => r.DiscountAmount).GreaterThanOrEqualTo(0m);
        RuleForEach(r => r.Items).SetValidator(new InvoiceLineRequestValidator());
        RuleForEach(r => r.Payments).SetValidator(new PaymentRequestValidator());

        // A POS sale needs at least one explicit line, unless it is tied to a visit whose dispensed
        // meds / procedures the server will auto-assemble.
        RuleFor(r => r)
            .Must(r => r.Items.Count > 0 || r.VisitId is not null)
            .WithMessage("Provide at least one line item, or link a visit to auto-assemble its charges.");
    }
}

public sealed class FieldInvoiceRequestValidator : AbstractValidator<FieldInvoiceRequest>
{
    public FieldInvoiceRequestValidator()
    {
        RuleFor(r => r.IdempotencyKey).NotEmpty();
        RuleFor(r => r.DiscountAmount).GreaterThanOrEqualTo(0m);
        RuleForEach(r => r.Items).SetValidator(new InvoiceLineRequestValidator());
        RuleForEach(r => r.Payments).SetValidator(new PaymentRequestValidator());
    }
}

public sealed class ExamFeeInvoiceRequestValidator : AbstractValidator<ExamFeeInvoiceRequest>
{
    public ExamFeeInvoiceRequestValidator()
    {
        RuleFor(r => r.IdempotencyKey).NotEmpty();
        RuleFor(r => r.Amount!.Value).GreaterThanOrEqualTo(0m).When(r => r.Amount.HasValue);
        RuleForEach(r => r.Payments).SetValidator(new PaymentRequestValidator());
    }
}

public sealed class ReceiptVoucherRequestValidator : AbstractValidator<ReceiptVoucherRequest>
{
    public ReceiptVoucherRequestValidator()
    {
        RuleFor(r => r.CustomerId).NotEmpty();
        RuleFor(r => r.Amount).GreaterThan(0m);
        RuleFor(r => r.Method)
            .Must(PaymentMethod.All.Contains)
            .WithMessage($"method must be one of: {string.Join(", ", PaymentMethod.All)}.");
        RuleFor(r => r.IdempotencyKey).NotEmpty();
    }
}
