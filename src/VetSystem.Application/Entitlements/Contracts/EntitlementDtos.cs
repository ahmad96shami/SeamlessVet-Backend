namespace VetSystem.Application.Entitlements.Contracts;

/// <summary>A computed doctor entitlement (SCHEMA §8). Server-authoritative — clients only read it.</summary>
public sealed record DoctorEntitlementResponse(
    Guid Id,
    Guid DoctorId,
    Guid? BatchId,
    Guid? VisitId,
    string CalculationSystem,
    decimal ComputedAmount,
    decimal? CeilingApplied,
    string Status,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt,
    string? PaidMethod,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Disbursement payload for <c>POST /doctor-entitlements/{id}/pay</c>. <see cref="Method"/>
/// is a <see cref="VetSystem.Domain.Entities.PaymentMethod"/> value.</summary>
public sealed record PayEntitlementRequest(string Method);

/// <summary>Result of <c>POST /customers/{id}/close-account</c>: the now-closed ledger plus the
/// entitlements the settlement workflow produced/refreshed for the customer (PRD §7.7).</summary>
public sealed record CloseAccountResponse(
    Guid CustomerId,
    Guid LedgerId,
    string Status,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<DoctorEntitlementResponse> Entitlements);
