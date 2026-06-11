namespace VetSystem.Application.Entitlements.Contracts;

/// <summary>A computed doctor entitlement (SCHEMA §8; M30 — batch-only, immutable accrual audit).
/// Server-authoritative — clients only read it.</summary>
public sealed record DoctorEntitlementResponse(
    Guid Id,
    Guid DoctorId,
    Guid BatchId,
    string CalculationSystem,
    decimal ComputedAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Result of <c>POST /customers/{id}/close-account</c> (M16: own ledger) and
/// <c>POST /farms/{id}/close-account</c>: the now-closed ledger plus the entitlements already on record
/// for that owner's batches (M30 — closing no longer computes them). <see cref="FarmId"/> is set for a
/// farm close (with <see cref="CustomerId"/> the owning customer), null for a customer close.</summary>
public sealed record CloseAccountResponse(
    Guid CustomerId,
    Guid? FarmId,
    Guid LedgerId,
    string Status,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<DoctorEntitlementResponse> Entitlements);
