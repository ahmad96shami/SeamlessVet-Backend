namespace VetSystem.Application.Financial.Contracts;

/// <summary>
/// One requested invoice line. Targets a product <b>or</b> a service (exactly one id). <c>UnitPrice</c>
/// is optional: when omitted the server uses the catalog price (<c>products.selling_price</c> /
/// <c>services.default_price</c>) — M8's pricing service later swaps in contract-overridden prices.
/// <c>CostPrice</c> is never client-supplied here; the server snapshots it from the product at sale
/// time (SCHEMA "Key invariants" #8).
/// <para>
/// A line may back-link one of the request visit's charges via <c>PrescriptionId</c> (with the
/// matching <c>ProductId</c>), <c>ProcedureId</c>, or <c>VaccinationId</c> (each with the matching
/// <c>ServiceId</c>) so the POS can present visit charges as editable cart lines (price/discount).
/// The server then resolves the line from the clinical record — <b>quantity is
/// server-authoritative</b> (the prescription's / 1 for a procedure or vaccination; the client
/// value is ignored) — and skips that charge during auto-assembly.
/// </para>
/// </summary>
public sealed record InvoiceLineRequest(
    Guid? ProductId,
    Guid? ServiceId,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountAmount = 0m,
    Guid? PrescriptionId = null,
    Guid? ProcedureId = null,
    Guid? VaccinationId = null);

/// <summary>
/// A payment leg. Several may be sent for a mixed payment (PRD §5.4). M19: a <c>cheque</c> leg may
/// carry optional metadata and settles immediately (like cash).
/// </summary>
public sealed record PaymentRequest(
    Guid? Id,
    string Method,
    decimal Amount,
    string? ChequeNumber = null,
    string? ChequeBank = null,
    DateOnly? ChequeDueDate = null);

/// <summary>
/// POS issuance (M7 task 3). <c>CustomerId</c> null = walk-in (no ledger posting, PRD §5.4).
/// When <c>VisitId</c> is set, the server auto-assembles that visit's unbilled
/// <c>dispensed_to_owner</c> prescriptions and billable procedures as additional lines (task 8).
/// <c>IdempotencyKey</c> is the row-level dedupe (SCHEMA invoices.idempotency_key) — stable per
/// logical invoice across the online and sync paths.
/// </summary>
public sealed record PosInvoiceRequest(
    Guid? Id,
    Guid? CustomerId,
    Guid? VisitId,
    string? Number,
    decimal DiscountAmount,
    IReadOnlyList<InvoiceLineRequest> Items,
    IReadOnlyList<PaymentRequest> Payments,
    string IdempotencyKey);

/// <summary>
/// Field-visit invoice (M7 task 5). Linked to a visit and optionally a batch (drug-profit calc,
/// M9). Products deduct from the visit doctor's field inventory (PRD §6.2). Same line/payment shape
/// as POS; the visit's unbilled dispensed meds + procedures auto-assemble.
/// </summary>
public sealed record FieldInvoiceRequest(
    Guid? Id,
    Guid? BatchId,
    string? Number,
    decimal DiscountAmount,
    IReadOnlyList<InvoiceLineRequest> Items,
    IReadOnlyList<PaymentRequest> Payments,
    string IdempotencyKey);

/// <summary>
/// Standalone exam-fee invoice (Kashfiyya; M7 task 6, System B input for M9). No line items / no
/// inventory: the whole invoice is the fee. When <c>Amount</c> is omitted the server falls back to
/// the visit's <c>exam_fee_applied</c>, then <c>system_settings.default_exam_fee</c>.
/// </summary>
public sealed record ExamFeeInvoiceRequest(
    Guid? Id,
    string? Number,
    decimal? Amount,
    IReadOnlyList<PaymentRequest> Payments,
    string IdempotencyKey);

public sealed record InvoiceItemResponse(
    Guid Id,
    Guid InvoiceId,
    Guid? ProductId,
    Guid? ServiceId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal CostPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    Guid? PrescriptionId,
    Guid? ProcedureId,
    Guid? VaccinationId);

public sealed record PaymentResponse(
    Guid Id,
    Guid InvoiceId,
    string Method,
    decimal Amount,
    DateTimeOffset PaidAt,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate);

public sealed record InvoiceResponse(
    Guid Id,
    string InvoiceType,
    Guid? CustomerId,
    Guid? FarmId,
    Guid? VisitId,
    Guid? BatchId,
    string? Number,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    string Status,
    Guid IssuedBy,
    DateTimeOffset IssuedAt,
    Guid? VoidOfInvoiceId,
    IReadOnlyList<InvoiceItemResponse> Items,
    IReadOnlyList<PaymentResponse> Payments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Receipt voucher (Sanad Qabd) issuance (M7 task 9). M16: an optional <see cref="FarmId"/> credits
/// the payment to that farm's ledger instead of the customer's own ledger (the farm must belong to
/// <see cref="CustomerId"/>).
/// </summary>
public sealed record ReceiptVoucherRequest(
    Guid? Id,
    Guid CustomerId,
    Guid? FarmId,
    decimal Amount,
    string Method,
    string? Notes,
    string IdempotencyKey,
    string? ChequeNumber = null,
    string? ChequeBank = null,
    DateOnly? ChequeDueDate = null);

public sealed record ReceiptVoucherResponse(
    Guid Id,
    Guid CustomerId,
    Guid? FarmId,
    decimal Amount,
    string Method,
    Guid IssuedBy,
    DateTimeOffset IssuedAt,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    DateTimeOffset CreatedAt);
