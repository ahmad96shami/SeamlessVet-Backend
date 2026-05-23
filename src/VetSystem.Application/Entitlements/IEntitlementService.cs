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

    /// <summary>
    /// The full drug-profit accounting for a batch <b>without persisting</b> — built from the exact
    /// same inputs <see cref="ComputeForBatchAsync"/> uses (effective invoices, contract-resolved
    /// sale values, cost snapshots, exam-fee model, toggle, System A/B). The profit-per-batch report
    /// (M12 tasks 4/17) consumes this so its doctor/clinic split reconciles to the persisted
    /// entitlement to the cent. Throws <see cref="VetSystem.Domain.Common.NotFoundException"/> when the
    /// batch does not exist.
    /// </summary>
    Task<BatchEntitlementBreakdown> ExplainForBatchAsync(Guid batchId, CancellationToken cancellationToken);
}

/// <summary>
/// The drug-profit accounting for one batch (PRD §7.4, §7.9). <see cref="DoctorShare"/> equals the
/// computed <see cref="DoctorEntitlement.ComputedAmount"/> for the batch. <see cref="ClinicShare"/> is
/// the drug profit the clinic keeps: under System A it is <c>DrugProfit − DoctorShare</c> (the doctor's
/// share comes out of drug profit); under System B or when the toggle is disabled it is the whole
/// <c>DrugProfit</c> (the doctor's fee, if any, is paid separately — not out of drug profit).
/// </summary>
public sealed record BatchEntitlementBreakdown(
    Guid BatchId,
    Guid DoctorId,
    Guid CustomerId,
    DateOnly? EndDate,
    string System,
    bool Enabled,
    decimal Revenue,
    decimal DrugCost,
    decimal DrugProfit,
    decimal ExamFee,
    decimal DoctorShare,
    decimal? CeilingApplied,
    decimal ClinicShare);
