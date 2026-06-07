using FluentAssertions;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Application.Financial.Validators;

namespace VetSystem.Tests.Financial;

/// <summary>
/// M7 task 16 (unit) — static-shape validation for the issuance requests: the product-XOR-service
/// line target, positive quantities/amounts, valid payment methods, and the "at least one line or a
/// linked visit" rule. DB-aware reconciliation (payment-sum ≤ total, cost snapshot) lives in the
/// service and is covered by the integration tests.
/// </summary>
public sealed class InvoiceValidatorTests
{
    private static readonly PosInvoiceRequestValidator Pos = new();
    private static readonly ReceiptVoucherRequestValidator Voucher = new();

    private static PosInvoiceRequest Build(
        IReadOnlyList<InvoiceLineRequest>? items = null,
        IReadOnlyList<PaymentRequest>? payments = null,
        Guid? visitId = null,
        string idempotencyKey = "idem-key-123")
        => new(
            Id: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            VisitId: visitId,
            Number: null,
            DiscountAmount: 0m,
            Items: items ?? [new InvoiceLineRequest(Guid.CreateVersion7(), null, null, 1m, 10m)],
            Payments: payments ?? [new PaymentRequest(null, "cash", 10m)],
            IdempotencyKey: idempotencyKey);

    [Fact]
    public void Pos_ValidRequest_Passes() => Pos.Validate(Build()).IsValid.Should().BeTrue();

    [Fact]
    public void Pos_LineWithBothProductAndService_Fails()
    {
        var req = Build([new InvoiceLineRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), null, 1m, 10m)]);
        Pos.Validate(req).IsValid.Should().BeFalse("a line targets exactly one of product / service");
    }

    [Fact]
    public void Pos_LineWithNeitherProductNorService_Fails()
    {
        var req = Build([new InvoiceLineRequest(null, null, "freeform", 1m, 10m)]);
        Pos.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Pos_NonPositiveQuantity_Fails()
    {
        var req = Build([new InvoiceLineRequest(Guid.CreateVersion7(), null, null, 0m, 10m)]);
        Pos.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Pos_UnknownPaymentMethod_Fails()
    {
        var req = Build(payments: [new PaymentRequest(null, "bitcoin", 10m)]);
        Pos.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Pos_NonPositivePaymentAmount_Fails()
    {
        var req = Build(payments: [new PaymentRequest(null, "cash", 0m)]);
        Pos.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Pos_EmptyItemsWithoutVisit_Fails()
    {
        var req = Build(items: [], payments: []);
        Pos.Validate(req).IsValid.Should().BeFalse("a POS sale needs an explicit line or a linked visit");
    }

    [Fact]
    public void Pos_EmptyItemsWithVisit_Passes()
    {
        // Lines auto-assemble from the visit's dispensed meds / procedures.
        var req = Build(items: [], payments: [], visitId: Guid.CreateVersion7());
        Pos.Validate(req).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Pos_BlankIdempotencyKey_Fails()
        => Pos.Validate(Build(idempotencyKey: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Pos_BackLinkedLine_BothPrescriptionAndProcedure_Fails()
    {
        var req = Build(
            [new InvoiceLineRequest(Guid.CreateVersion7(), null, null, 1m, 10m,
                PrescriptionId: Guid.CreateVersion7(), ProcedureId: Guid.CreateVersion7())],
            visitId: Guid.CreateVersion7());
        Pos.Validate(req).IsValid.Should().BeFalse("a line back-links a prescription or a procedure, not both");
    }

    [Fact]
    public void Pos_PrescriptionLinkOnServiceLine_Fails()
    {
        var req = Build(
            [new InvoiceLineRequest(null, Guid.CreateVersion7(), null, 1m, 10m, PrescriptionId: Guid.CreateVersion7())],
            visitId: Guid.CreateVersion7());
        Pos.Validate(req).IsValid.Should().BeFalse("a prescription-linked line must carry the product, not a service");
    }

    [Fact]
    public void Pos_ProcedureLinkOnProductLine_Fails()
    {
        var req = Build(
            [new InvoiceLineRequest(Guid.CreateVersion7(), null, null, 1m, 10m, ProcedureId: Guid.CreateVersion7())],
            visitId: Guid.CreateVersion7());
        Pos.Validate(req).IsValid.Should().BeFalse("a procedure-linked line must carry the service, not a product");
    }

    [Fact]
    public void Pos_DuplicateBackLinks_Fail()
    {
        var rxId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var req = Build(
            [
                new InvoiceLineRequest(productId, null, null, 1m, 10m, PrescriptionId: rxId),
                new InvoiceLineRequest(productId, null, null, 1m, 10m, PrescriptionId: rxId),
            ],
            visitId: Guid.CreateVersion7());
        Pos.Validate(req).IsValid.Should().BeFalse("each prescription may be billed by at most one line");
    }

    [Fact]
    public void Pos_BackLinkedLines_WellFormed_Pass()
    {
        var req = Build(
            [
                new InvoiceLineRequest(Guid.CreateVersion7(), null, null, 1m, 10m, PrescriptionId: Guid.CreateVersion7()),
                new InvoiceLineRequest(null, Guid.CreateVersion7(), null, 1m, 35m, ProcedureId: Guid.CreateVersion7()),
            ],
            visitId: Guid.CreateVersion7());
        Pos.Validate(req).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Voucher_Valid_Passes()
    {
        var req = new ReceiptVoucherRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), null, 50m, "cash", null, "idem-1");
        Voucher.Validate(req).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Voucher_NonPositiveAmount_Fails(decimal amount)
    {
        var req = new ReceiptVoucherRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), null, amount, "cash", null, "idem-1");
        Voucher.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Voucher_UnknownMethod_Fails()
    {
        var req = new ReceiptVoucherRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), null, 50m, "voucher", null, "idem-1");
        Voucher.Validate(req).IsValid.Should().BeFalse();
    }
}
