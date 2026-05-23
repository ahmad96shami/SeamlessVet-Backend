using VetSystem.Domain.Common;

namespace VetSystem.Domain.Events;

/// <summary>
/// Raised when a <c>dispensed_to_owner</c> prescription is created (M5 task 10). Unlike an
/// <c>administered_in_clinic</c> prescription — which deducts from inventory immediately — a
/// dispensed item is sold to the owner, so it is queued for the visit's POS invoice. M7 subscribes
/// and auto-appends a line to the visit's open invoice (resolving the price via M8's pricing
/// service). For now the in-memory publisher just records it.
/// </summary>
public sealed record PrescriptionDispensedEvent(
    Guid EnvironmentId,
    Guid PrescriptionId,
    Guid VisitId,
    Guid CustomerId,
    Guid ProductId,
    decimal Quantity,
    Guid DoctorId) : IDomainEvent;
