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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
