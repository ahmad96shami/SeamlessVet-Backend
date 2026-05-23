using VetSystem.Domain.Common;

namespace VetSystem.Domain.Events;

/// <summary>
/// Raised when a <c>doctor_entitlements</c> row is approved for payout (M9 approve action, M11 task 13).
/// M11 pushes a realtime notification to the entitled doctor so they know their accrual cleared the
/// settlement lock and is approved for disbursement (PRD §9).
/// </summary>
public sealed record EntitlementApprovedEvent(
    Guid EnvironmentId,
    Guid EntitlementId,
    Guid DoctorId,
    decimal ComputedAmount,
    Guid ApprovedBy) : IDomainEvent;
