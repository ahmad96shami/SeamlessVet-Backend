using VetSystem.Domain.Entities;

namespace VetSystem.Application.Entitlements;

/// <summary>
/// M28 — the reformulated entitlement split (client decisions 2026-06-11). The supervision fee
/// (the <see cref="IExamFeeCalculator"/> output) <b>is</b> the doctor's entitlement in <i>both</i>
/// systems — there is no percentage, no ceiling, and <b>no clamp</b> (the full fee is paid even when it
/// exceeds drug profit, so the clinic's share may go negative). System A vs B is an <i>accounting-only</i>
/// difference: who funds the fee.
///
/// <list type="bullet">
/// <item><b>System A (drug_profit):</b> the fee is carved out of drug profit — the farmer pays normal
/// drug prices, the clinic hands the doctor the fee from its own margin
/// (<c>clinicShare = drugProfit − fee − discount</c>).</item>
/// <item><b>System B (direct_fee):</b> the fee is charged to the farmer on top of the drugs (it lands in
/// the settled total via a ledger adjustment), then passed to the doctor
/// (<c>clinicShare = drugProfit − discount</c>).</item>
/// <item><b>Toggle OFF</b> (the doctor is a salaried employee — the clinic keeps the fee): the doctor
/// gets nothing. System A keeps its full margin (<c>drugProfit − discount</c>); System B still charges
/// the farmer but the clinic retains the fee (<c>drugProfit − discount + fee</c>).</item>
/// </list>
///
/// <para>The fee/discount are applied <b>after</b> any settlement-time reprice (they sit on the settled
/// drug profit). Pure (no DB) so the full {system} × {toggle} × {discount} matrix unit-tests in
/// isolation. The single unifying identity is
/// <c>clinicShare = drugProfit + feeAddedToSettlement − discount − doctorShare</c>.</para>
/// </summary>
public static class EntitlementSplitCalculator
{
    /// <param name="system">The batch's <see cref="EntitlementSystem"/> (drug_profit | direct_fee).</param>
    /// <param name="enabled">The resolved entitlement toggle (per-batch override ?? global default).</param>
    /// <param name="drugProfit">Σ(saleValue − cost) × qty over the batch's effective product lines.</param>
    /// <param name="supervisionFee">The <see cref="IExamFeeCalculator"/> output (already rounded).</param>
    /// <param name="settlementDiscount">The M24 batch-settlement خصم (0 when unsettled).</param>
    public static EntitlementSplit Resolve(
        string system, bool enabled, decimal drugProfit, decimal supervisionFee, decimal settlementDiscount)
    {
        var fee = Round(supervisionFee);
        var isDirectFee = system == EntitlementSystem.DirectFee;

        // The entitlement IS the fee in both systems — no clamp; 0 only when the toggle is off.
        var doctorShare = enabled ? fee : 0m;

        // System B charges the farmer the fee on top (it enters the settled total + owner ledger);
        // System A funds it from the clinic's own drug margin, so nothing is added to the farmer's bill.
        var feeAddedToSettlement = isDirectFee ? fee : 0m;

        // When the toggle is off in System B the farmer was still charged, but the doctor gets nothing —
        // the clinic keeps the fee. (System A off charges no fee at all, so nothing is retained.)
        var feeRetainedByClinic = isDirectFee && !enabled ? fee : 0m;

        var clinicShare = drugProfit + feeAddedToSettlement - settlementDiscount - doctorShare;

        return new EntitlementSplit(doctorShare, clinicShare, feeAddedToSettlement, feeRetainedByClinic);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>The resolved M28 split. <see cref="DoctorShare"/> is the persisted entitlement amount;
/// <see cref="ClinicShare"/> is what the clinic keeps (may be negative). <see cref="FeeAddedToSettlement"/>
/// is the System-B fee charged to the farmer (0 for System A); <see cref="FeeRetainedByClinic"/> is the
/// fee the clinic keeps when the toggle is off in System B (0 otherwise).</summary>
public sealed record EntitlementSplit(
    decimal DoctorShare,
    decimal ClinicShare,
    decimal FeeAddedToSettlement,
    decimal FeeRetainedByClinic);
