namespace VetSystem.Application.Purchasing.Contracts;

/// <summary>
/// Supplier-payment issuance (M19 task 6). Posting one reduces the supplier's ledger balance. The
/// supplier is taken from the route, not the body. <c>Method</c> is one of cash / card / bank_transfer
/// / cheque (never credit). Cheque metadata is optional and stored when <c>Method</c> is <c>cheque</c>;
/// a cheque settles immediately.
/// </summary>
public sealed record SupplierPaymentRequest(
    Guid? Id,
    decimal Amount,
    string Method,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    string IdempotencyKey);

public sealed record SupplierPaymentResponse(
    Guid Id,
    Guid SupplierId,
    decimal Amount,
    string Method,
    Guid PaidBy,
    DateTimeOffset PaidAt,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    DateTimeOffset CreatedAt);
