using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — what a field doctor is owed from a batch's supervision fee (PRD §7.4, §7.8).
///
/// <para><b>M30:</b> entitlements are <b>batch-only</b> and an <b>immutable accrual audit</b>. The row
/// is written exactly once, when the batch is settled (تصفية), at which point the same amount is
/// credited to the doctor's <see cref="DoctorPartnerLedger"/> — that ledger is the live payable, and
/// the approve/pay/settlement-lock lifecycle is gone. (The per-visit System-B entitlement and the
/// customer/farm settlement lock were removed in M30.)</para>
///
/// <para>Server-authoritative on sync (M9 task 13): clients pull entitlements read-only; the row is
/// computed by the settlement workflow through the online API, mirroring batches and stock items.</para>
/// </summary>
public sealed class DoctorEntitlement : Entity
{
    public Guid DoctorId { get; set; }

    /// <summary>The batch (Dawra) that sourced this entitlement.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Which mechanism produced <see cref="ComputedAmount"/> (PRD §7.4): drug_profit | direct_fee.
    /// Accounting-only since M28 — the amount is the supervision fee under both.</summary>
    public string CalculationSystem { get; set; } = EntitlementSystem.DrugProfit;

    /// <summary>The doctor's entitlement — the supervision fee in full when the toggle is enabled, else 0
    /// (M28: no percentage, no ceiling, no clamp).</summary>
    public decimal ComputedAmount { get; set; }
}
