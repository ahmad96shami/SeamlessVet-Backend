using VetSystem.Domain.Entities;

namespace VetSystem.Application.Entitlements;

/// <summary>
/// Computes a doctor's entitlement and writes it as a <c>pending</c> <see cref="DoctorEntitlement"/>
/// row (M9 task 8). Idempotent: re-running for the same source updates the existing pending row
/// rather than duplicating, and leaves an already-settled (approved/paid) row untouched.
///
/// <para>The settlement workflow (account close, PRD §7.7) calls these; both honour the toggle
/// (SCHEMA invariant #4 — disabled ⇒ all profit to the clinic, computed 0) and resolve System A's
/// <c>sale_value</c> via <see cref="VetSystem.Application.Contracts.IPricingService"/> (task 6).</para>
/// </summary>
public interface IEntitlementService
{
    /// <summary>Drug-profit (System A) or direct-fee (System B) over the batch's invoices, per the
    /// batch's <c>entitlement_system</c>. The responsible doctor is credited.</summary>
    Task<DoctorEntitlement> ComputeForBatchAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>System B direct fee from the visit's standalone exam-fee invoice(s). Returns null when
    /// the visit has no exam-fee basis (nothing to credit).</summary>
    Task<DoctorEntitlement?> ComputeForVisitAsync(Guid visitId, CancellationToken cancellationToken);
}
