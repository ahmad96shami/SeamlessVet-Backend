namespace VetSystem.Application.DailyFollowUps.Contracts;

/// <summary>
/// SCHEMA §6 daily follow-up payload (PRD §5.2-E) — a per-day entry for a hospitalized case.
/// Clinic-only: the service rejects creation against a field visit. <c>Id</c> is client-generated.
/// </summary>
public sealed record DailyFollowUpCreateRequest(
    Guid? Id,
    Guid VisitId,
    DateOnly EntryDate,
    string? Condition,
    decimal? Temperature,
    int? HeartRate,
    int? RespiratoryRate,
    string? AdministeredMeds,
    string? Notes);

public sealed record DailyFollowUpPatchRequest(
    DateOnly? EntryDate,
    string? Condition,
    decimal? Temperature,
    int? HeartRate,
    int? RespiratoryRate,
    string? AdministeredMeds,
    string? Notes);

public sealed record DailyFollowUpResponse(
    Guid Id,
    Guid VisitId,
    DateOnly EntryDate,
    string? Condition,
    decimal? Temperature,
    int? HeartRate,
    int? RespiratoryRate,
    string? AdministeredMeds,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
