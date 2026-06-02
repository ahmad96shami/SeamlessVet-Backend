namespace VetSystem.Application.NightStays.Contracts;

/// <summary>
/// SCHEMA §6 night-stay payload (مبيت, PRD §18.6) — a hospitalized boarding episode. Clinic-only:
/// the service rejects creation against a field visit. <c>Id</c> is client-generated. A stay is
/// created <b>open</b> (no check-out); the per-night rate is snapshotted from <c>system_settings</c>
/// by <see cref="CareType"/> unless an explicit <see cref="NightlyRate"/> override is supplied.
/// Closing the stay (<c>POST /night-stays/{id}/close</c>) computes the nights and posts the charge.
/// </summary>
public sealed record NightStayCreateRequest(
    Guid? Id,
    Guid VisitId,
    string CareType,
    DateTimeOffset? CheckInAt,
    decimal? NightlyRate,
    string? Notes);

/// <summary>
/// Partial edit of an <b>open</b> stay. Billing-affecting fields (<see cref="CareType"/>,
/// <see cref="CheckInAt"/>, <see cref="NightlyRate"/>) are rejected once the stay is closed/charged
/// (append-only money); notes stay editable.
/// </summary>
public sealed record NightStayPatchRequest(
    string? CareType,
    DateTimeOffset? CheckInAt,
    decimal? NightlyRate,
    string? Notes);

/// <summary>Closes a stay and bills <c>nights × nightly_rate</c>. <c>CheckOutAt</c> defaults to now.</summary>
public sealed record NightStayCloseRequest(
    DateTimeOffset? CheckOutAt);

public sealed record NightStayResponse(
    Guid Id,
    Guid VisitId,
    string CareType,
    DateTimeOffset CheckInAt,
    DateTimeOffset? CheckOutAt,
    int NightsCount,
    decimal NightlyRate,
    decimal Total,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
