using VetSystem.Domain.Common;

namespace VetSystem.Domain.Events;

/// <summary>
/// Raised when a visit is created and assigned to a doctor by a <b>different</b> user (e.g. a
/// receptionist registering an in-clinic visit). M11's notification handler turns this into a
/// per-user, realtime alert for the assigned doctor so they know a visit is waiting for them.
/// Not raised when the doctor created their own visit (the field-doctor offline flow), since the
/// caller only publishes it when the creator differs from <see cref="DoctorId"/>.
/// </summary>
public sealed record VisitAssignedEvent(
    Guid EnvironmentId,
    Guid VisitId,
    string? VisitNumber,
    Guid DoctorId,
    Guid CustomerId,
    string VisitType,
    Guid? AssignedByUserId) : IDomainEvent;
