namespace VetSystem.Application.DoctorPartners.Contracts;

/// <summary>
/// M30 (SCHEMA §4) doctor-partner payload. <c>UserId</c> is the <b>mandatory</b> staff account this
/// partner pays (one partner per user). <c>Id</c> may be supplied as a Guid v7 (CRUD is online-only —
/// doctor-partners are not part of any sync scope — but the client-id convention is kept for
/// consistency).
/// </summary>
public sealed record DoctorPartnerRequest(
    Guid? Id,
    Guid UserId,
    string? Notes);

public sealed record DoctorPartnerPatchRequest(
    string? Notes);

/// <summary>
/// Doctor-partner read projection. <see cref="DoctorName"/> is resolved from the linked user (not
/// stored). <see cref="Balance"/> and <see cref="LedgerStatus"/> are the partner's ledger state —
/// positive balance = the clinic owes the doctor. Read-only (the ledger is server-authoritative).
/// </summary>
public sealed record DoctorPartnerResponse(
    Guid Id,
    Guid UserId,
    string DoctorName,
    string? Notes,
    decimal Balance,
    string LedgerStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
