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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
