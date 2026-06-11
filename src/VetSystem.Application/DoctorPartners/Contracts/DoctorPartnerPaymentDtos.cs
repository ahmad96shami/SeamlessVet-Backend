namespace VetSystem.Application.DoctorPartners.Contracts;

/// <summary>
/// Doctor-partner payment issuance (M30). Posting one reduces the doctor's ledger balance. The partner
/// is taken from the route, not the body. <c>Method</c> is one of cash / card / bank_transfer / cheque
/// (never credit). Cheque metadata is optional and stored when <c>Method</c> is <c>cheque</c>; a cheque
/// settles immediately.
/// </summary>
public sealed record DoctorPartnerPaymentRequest(
    Guid? Id,
    decimal Amount,
    string Method,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    string IdempotencyKey);

public sealed record DoctorPartnerPaymentResponse(
    Guid Id,
    Guid DoctorPartnerId,
    decimal Amount,
    string Method,
    Guid PaidBy,
    DateTimeOffset PaidAt,
    string? Notes,
    string? ChequeNumber,
    string? ChequeBank,
    DateOnly? ChequeDueDate,
    DateTimeOffset CreatedAt);
