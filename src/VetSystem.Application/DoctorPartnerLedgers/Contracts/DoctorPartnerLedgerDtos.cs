namespace VetSystem.Application.DoctorPartnerLedgers.Contracts;

/// <summary>
/// Append-only doctor-partner-ledger entry payload (SCHEMA §4). The single write path for every
/// doctor-AP event: batch entitlement credits (M30), doctor-partner payments (M30), manual
/// adjustments. <c>IdempotencyKey</c> is mandatory and unique per environment so a retried post
/// collapses to one row.
/// </summary>
public sealed record DoctorPartnerLedgerEntryRequest(
    Guid? Id,
    Guid DoctorPartnerLedgerId,
    string EntryType,
    decimal Amount,
    Guid? DoctorEntitlementId,
    Guid? BatchId,
    Guid? DoctorPartnerPaymentId,
    string? Description,
    string IdempotencyKey);

public sealed record DoctorPartnerLedgerEntryResponse(
    Guid Id,
    Guid DoctorPartnerLedgerId,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    Guid? DoctorEntitlementId,
    Guid? BatchId,
    Guid? DoctorPartnerPaymentId,
    string? Description,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

/// <summary>
/// Doctor-partner account statement returned by <c>GET /doctor-partners/{id}/statement</c>. Mirrors the
/// supplier statement: <see cref="OpeningBalance"/> is the running balance at <c>from</c> (0 if none);
/// <see cref="ClosingBalance"/> is the balance after the last entry in range. Positive = the clinic owes
/// the doctor. Drives the print / share client path.
/// </summary>
public sealed record DoctorPartnerStatementResponse(
    Guid DoctorPartnerId,
    string DoctorName,
    Guid LedgerId,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<DoctorPartnerLedgerEntryResponse> Entries);
