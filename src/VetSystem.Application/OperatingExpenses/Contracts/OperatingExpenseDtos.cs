namespace VetSystem.Application.OperatingExpenses.Contracts;

/// <summary>A row in the operating-expenses list (GET /operating-expenses).</summary>
public sealed record OperatingExpenseResponse(
    Guid Id,
    string Category,
    decimal Amount,
    DateOnly IncurredOn,
    bool Paid,
    DateTimeOffset? PaidAt,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>POST /operating-expenses — record an operating expense (e.g. a water or electricity bill).</summary>
public sealed record CreateOperatingExpenseRequest(
    Guid? Id,
    string Category,
    decimal Amount,
    DateOnly IncurredOn,
    bool Paid,
    string? Note);

/// <summary>
/// PATCH /operating-expenses/{id} — partial update. Null fields are left unchanged. Setting
/// <see cref="Paid"/> true stamps the payment time; setting it false clears it.
/// </summary>
public sealed record UpdateOperatingExpenseRequest(
    string? Category,
    decimal? Amount,
    DateOnly? IncurredOn,
    bool? Paid,
    string? Note);
