namespace VetSystem.Application.Entitlements;

/// <summary>
/// System B — direct examination fee (PRD §7.4): the exam/supervision fee is credited to the doctor
/// in full, not tied to drug profit. The caller resolves the fee first (a standalone <c>exam_fee</c>
/// invoice's total for a visit). Used by the visit-sourced entitlement path; the batch path uses
/// <see cref="EntitlementSplitCalculator"/> directly (M28).
/// </summary>
public interface ISystemBDirectFeeCalculator
{
    EntitlementAmount Calculate(decimal fee);
}

/// <inheritdoc cref="ISystemBDirectFeeCalculator"/>
public sealed class SystemBDirectFeeCalculator : ISystemBDirectFeeCalculator
{
    public EntitlementAmount Calculate(decimal fee)
        => new(Math.Round(fee, 2, MidpointRounding.AwayFromZero));
}

/// <summary>A computed entitlement amount. (M28 retired System A's ceiling, so there is no cap to
/// report anymore — the fee is credited in full.)</summary>
public sealed record EntitlementAmount(decimal ComputedAmount);
