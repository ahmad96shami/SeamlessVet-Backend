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
        // M23 — a night-stay / checkup-fee back-linked line may omit BOTH catalog ids: the server
        // resolves the per-environment system service itself (the persisted row always satisfies
        // the DB's product-XOR-service CHECK). Every other line names exactly one target.
        RuleFor(l => l)
            .Must(l => (l.ProductId is not null) ^ (l.ServiceId is not null))
            .When(l => l.NightStayId is null && l.CheckupFeeVisitId is null)
            .WithMessage("Each line must reference exactly one of product_id or service_id.");
        RuleFor(l => l.ProductId)
            .Null()
            .When(l => l.NightStayId is not null || l.CheckupFeeVisitId is not null)
            .WithMessage("A night-stay / checkup-fee line is a service line; it cannot reference a product.");

        RuleFor(l => l.Quantity).GreaterThan(0m);
        RuleFor(l => l.UnitPrice!.Value).GreaterThanOrEqualTo(0m).When(l => l.UnitPrice.HasValue);
        RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0m);

        // Visit-charge back-links: at most one, and it must agree with the line's catalog target
        // (a prescription dispenses a product; a procedure performs a service; M26 — a vaccination
        // dispenses a stock product).
        RuleFor(l => l)
            .Must(l => new[] { l.PrescriptionId, l.ProcedureId, l.VaccinationId, l.NightStayId, l.CheckupFeeVisitId }
                .Count(b => b is not null) <= 1)
            .WithMessage("A line may back-link one visit charge (prescription / procedure / vaccination / night stay / checkup fee) — at most one.");
        RuleFor(l => l.ProductId)
            .NotNull()
            .When(l => l.PrescriptionId is not null)
            .WithMessage("A prescription-linked line must reference the prescription's product_id.");
        RuleFor(l => l.ServiceId)
            .NotNull()
            .When(l => l.ProcedureId is not null)
            .WithMessage("A procedure-linked line must reference the procedure's service_id.");
        RuleFor(l => l.ProductId)
            .NotNull()
            .When(l => l.VaccinationId is not null)
            .WithMessage("A vaccination-linked line must reference the vaccine's product_id.");
    }
}

internal static class InvoiceLineRules
{
    /// <summary>No two lines may bill the same visit charge (the already-billed check in the
    /// service only sees committed invoices, not sibling lines of this request).</summary>
    public static bool BackLinksAreDistinct(IReadOnlyList<InvoiceLineRequest> items)
    {
        var rx = items.Where(i => i.PrescriptionId is not null).Select(i => i.PrescriptionId!.Value).ToList();
        var procedures = items.Where(i => i.ProcedureId is not null).Select(i => i.ProcedureId!.Value).ToList();
        var vaccinations = items.Where(i => i.VaccinationId is not null).Select(i => i.VaccinationId!.Value).ToList();
        var nightStays = items.Where(i => i.NightStayId is not null).Select(i => i.NightStayId!.Value).ToList();
        var checkupFees = items.Where(i => i.CheckupFeeVisitId is not null).Select(i => i.CheckupFeeVisitId!.Value).ToList();
        return rx.Distinct().Count() == rx.Count
               && procedures.Distinct().Count() == procedures.Count
               && vaccinations.Distinct().Count() == vaccinations.Count
               && nightStays.Distinct().Count() == nightStays.Count
               && checkupFees.Distinct().Count() == checkupFees.Count;
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
        RuleFor(r => r.Items)
            .Must(InvoiceLineRules.BackLinksAreDistinct)
            .WithMessage("Each prescription/procedure may be billed by at most one line.");

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
        RuleFor(r => r.Items)
            .Must(InvoiceLineRules.BackLinksAreDistinct)
            .WithMessage("Each prescription/procedure may be billed by at most one line.");
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
