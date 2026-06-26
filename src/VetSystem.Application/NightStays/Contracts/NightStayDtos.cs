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
    int? ExitHour,
    string? Notes);

/// <summary>
/// Partial edit of an <b>open</b> stay. Billing-affecting fields (<see cref="CareType"/>,
/// <see cref="CheckInAt"/>, <see cref="NightlyRate"/>) are rejected once the stay is closed/charged
/// (append-only money); notes + the intended exit hour stay editable (the latter never bills).
/// </summary>
public sealed record NightStayPatchRequest(
    string? CareType,
    DateTimeOffset? CheckInAt,
    decimal? NightlyRate,
    int? ExitHour,
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
    int? ExitHour,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // M23 — derived (not stored): the boarding charge is billed, via a POS invoice line back-link
    // OR the visit-completion ledger backstop (key night-stay-{id}). Mirrors BilledChargeGuard so the
    // UI can show «مُفوترة» + a lock even when the backstop (not the till) posted the charge. Default
    // false so Mapster's entity→DTO map and any positional construction stay valid; List/Get override it.
    bool Billed = false);
