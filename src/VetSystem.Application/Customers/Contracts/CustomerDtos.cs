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
/// Customer read projection. <c>Balance</c> and <c>LedgerStatus</c> are joined from the 1:1
/// <see cref="Ledger"/> (created alongside the customer) so the web list can show account state
/// at a glance and filter by it, without an N+1 statement call per row. Positive balance = the
/// customer owes the clinic. These two fields are read-only — the ledger is server-authoritative.
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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
