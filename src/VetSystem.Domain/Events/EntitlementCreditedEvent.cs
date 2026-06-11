using VetSystem.Domain.Common;

namespace VetSystem.Domain.Events;

/// <summary>
/// Raised when a batch settlement credits a doctor's entitlement to their
/// <c>doctor_partner_ledgers</c> balance (M30). M11 pushes a realtime notification to the doctor so
/// they know the supervision fee they earned on the cycle was posted to their account (PRD §9).
/// </summary>
public sealed record EntitlementCreditedEvent(
    Guid EnvironmentId,
    Guid EntitlementId,
    Guid DoctorId,
    Guid BatchId,
    decimal CreditAmount) : IDomainEvent;
