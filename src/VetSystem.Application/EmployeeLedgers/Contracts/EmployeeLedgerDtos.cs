namespace VetSystem.Application.EmployeeLedgers.Contracts;

/// <summary>
/// Append-only employee-ledger entry payload (SCHEMA §4). The single write path for every HR event:
/// monthly salary accruals (the job), salary payments, loans, loan repayments, and manual adjustments.
/// <c>IdempotencyKey</c> is mandatory and unique per environment so a retried post — including the
/// monthly accrual's <c>salary-accrual-{employeeId}-{yyyyMM}</c> period key — collapses to one row.
/// </summary>
public sealed record EmployeeLedgerEntryRequest(
    Guid? Id,
    Guid EmployeeLedgerId,
    string EntryType,
    decimal Amount,
    Guid? EmployeePaymentId,
    string? Description,
    string IdempotencyKey);

public sealed record EmployeeLedgerEntryResponse(
    Guid Id,
    Guid EmployeeLedgerId,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    Guid? EmployeePaymentId,
    string? Description,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

/// <summary>
/// Employee HR-account statement returned by <c>GET /employees/{id}/statement</c>. Mirrors the supplier
/// statement: <see cref="OpeningBalance"/> is the running balance at <c>from</c> (0 if none);
/// <see cref="ClosingBalance"/> is the balance after the last entry in range. Positive = the clinic owes
/// the employee unpaid salary; negative = the employee owes the clinic (an outstanding loan). Drives the
/// print / share client path.
/// </summary>
public sealed record EmployeeStatementResponse(
    Guid EmployeeId,
    string FullName,
    Guid LedgerId,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<EmployeeLedgerEntryResponse> Entries);
