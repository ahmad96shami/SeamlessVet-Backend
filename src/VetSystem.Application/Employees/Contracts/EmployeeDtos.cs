namespace VetSystem.Application.Employees.Contracts;

/// <summary>
/// M31 (SCHEMA §4) employee payload. <c>UserId</c> is the <b>optional</b> staff account this employee
/// maps to (null for a non-user employee such as a janitor); when present it is unique per environment.
/// <c>Id</c> may be supplied as a Guid v7 (CRUD is online-only — employees are not part of any sync scope
/// — but the client-id convention is kept for consistency).
/// </summary>
public sealed record EmployeeRequest(
    Guid? Id,
    Guid? UserId,
    string FullName,
    string? JobTitle,
    decimal MonthlySalary,
    bool Active,
    DateOnly? HiredAt,
    string? Notes);

public sealed record EmployeePatchRequest(
    string? FullName,
    string? JobTitle,
    decimal? MonthlySalary,
    bool? Active,
    DateOnly? HiredAt,
    string? Notes);

/// <summary>
/// Employee read projection. <see cref="Balance"/> and <see cref="LedgerStatus"/> are the employee's HR
/// ledger state — positive balance = the clinic owes unpaid salary, negative = an outstanding loan.
/// Read-only (the ledger is server-authoritative).
/// </summary>
public sealed record EmployeeResponse(
    Guid Id,
    Guid? UserId,
    string FullName,
    string? JobTitle,
    decimal MonthlySalary,
    bool Active,
    DateOnly? HiredAt,
    string? Notes,
    decimal Balance,
    string LedgerStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
