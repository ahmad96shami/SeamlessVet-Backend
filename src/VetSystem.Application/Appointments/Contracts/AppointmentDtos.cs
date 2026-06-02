namespace VetSystem.Application.Appointments.Contracts;

/// <summary>
/// SCHEMA §7 appointment payload (PRD §5.3). <c>Id</c> is client-generated (Guid v7) so dedicated
/// and sync writes converge offline-safely. Foreign keys are optional — a slot can be blocked for a
/// doctor before the customer/pet is known. An appointment may only be created <c>scheduled</c> or
/// <c>confirmed</c>; terminal states are reached via the dedicated endpoints.
/// </summary>
public sealed record AppointmentCreateRequest(
    Guid? Id,
    Guid? CustomerId,
    Guid? PetId,
    Guid? DoctorId,
    Guid? ServiceId,
    DateTimeOffset ScheduledAt,
    int? DurationMin,
    string? Status,
    string? Notes);

/// <summary>
/// Reschedule / edit (M6 tasks 2, 4). Every field is optional — only supplied ones change. Changing
/// <c>ScheduledAt</c>, <c>DurationMin</c>, or <c>DoctorId</c> re-runs conflict detection. <c>Status</c>
/// may advance <c>scheduled ↔ confirmed</c> but cannot close the appointment: use
/// <c>/attend</c>, <c>/cancel</c>, or <c>/no-show</c> for terminal transitions.
/// </summary>
public sealed record AppointmentPatchRequest(
    Guid? CustomerId,
    Guid? PetId,
    Guid? DoctorId,
    Guid? ServiceId,
    DateTimeOffset? ScheduledAt,
    int? DurationMin,
    string? Status,
    string? Notes);

public sealed record AppointmentResponse(
    Guid Id,
    Guid? CustomerId,
    Guid? PetId,
    Guid? DoctorId,
    Guid? ServiceId,
    DateTimeOffset ScheduledAt,
    int? DurationMin,
    string Status,
    string? Notes,
    Guid? VisitId,
    bool IsFollowUp,
    Guid? OriginVisitId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Schedules a follow-up appointment from a visit (M17 / PRD §18.8). The customer + pet are taken
/// from the origin visit; the doctor defaults to the origin's doctor. Attending the resulting
/// appointment opens a visit whose checkup fee is waived — exactly once per origin visit.
/// </summary>
public sealed record ScheduleFollowUpRequest(
    Guid? Id,
    DateTimeOffset ScheduledAt,
    Guid? DoctorId,
    int? DurationMin,
    string? Notes);
