namespace VetSystem.Application.Customers.Contracts;

/// <summary>
/// SCHEMA §2 customer payload. <c>Id</c> is supplied by the client as a Guid v7 to keep the
/// CRUD and sync paths interchangeable: a record created here will continue to update via
/// <c>/sync/customers</c> from the field device.
/// </summary>
public sealed record CustomerRequest(
    Guid? Id,
    string Type,
    string FullName,
    string? PhonePrimary,
    string? PhoneSecondary,
    string? Address,
    string? Email,
    string? IdNumber,
    string? Notes,
    Guid? AssignedDoctorId);

public sealed record CustomerPatchRequest(
    string? Type,
    string? FullName,
    string? PhonePrimary,
    string? PhoneSecondary,
    string? Address,
    string? Email,
    string? IdNumber,
    string? Notes,
    Guid? AssignedDoctorId);

/// <summary>
/// Customer read projection. M16: <c>Balance</c> and <c>LedgerStatus</c> are the customer's
/// <b>aggregate</b> across its own ledger (pet/clinic charges) and all its farm ledgers — own balance
/// + Σ farm balances. <c>LedgerStatus</c> is a <b>settled rollup</b>: <c>has_debt</c> when the
/// aggregate is positive, else <c>closed</c> when the customer's own ledger is closed (a zero-balance
/// farm ledger left open doesn't keep them open), else <c>open</c>. Positive balance = the customer
/// owes the clinic. <c>OwnBalance</c> is the customer's own (non-farm) ledger alone. <c>FarmLedgers</c>
/// is the per-farm breakdown — populated by the single-customer detail read, null on the list. All of
/// these are read-only — the ledgers are server-authoritative.
/// <para><c>OwnLedgerStatus</c> is the own (non-farm) ledger's status alone (vs. <c>LedgerStatus</c>
/// which is the aggregate). Clients posting to a specific ledger — e.g. a receipt voucher — need it
/// to tell an open own ledger from a closed one when the aggregate reads open only because a farm
/// ledger is open.</para>
/// </summary>
public sealed record CustomerResponse(
    Guid Id,
    string Type,
    string FullName,
    string? PhonePrimary,
    string? PhoneSecondary,
    string? Address,
    string? Email,
    string? IdNumber,
    string? Notes,
    Guid? AssignedDoctorId,
    decimal Balance,
    string LedgerStatus,
    decimal OwnBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CustomerFarmLedger>? FarmLedgers = null,
    string? OwnLedgerStatus = null);

/// <summary>M16 — one farm's ledger state in the customer detail breakdown.</summary>
public sealed record CustomerFarmLedger(
    Guid FarmId,
    string FarmName,
    Guid LedgerId,
    decimal Balance,
    string Status,
    DateTimeOffset? ClosedAt);
