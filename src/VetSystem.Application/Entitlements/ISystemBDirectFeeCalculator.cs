namespace VetSystem.Application.Entitlements;

/// <summary>
/// System B — direct examination fee (PRD §7.4): the exam/supervision fee is credited to the doctor
/// in full, not tied to drug profit. There is no ceiling (the fee is the agreed amount). The caller
/// resolves the fee first (a standalone <c>exam_fee</c> invoice's total for a visit, or the
/// supervision fee via <see cref="IExamFeeCalculator"/> for a batch).
/// </summary>
public interface ISystemBDirectFeeCalculator
{
    EntitlementAmount Calculate(decimal fee);
}

/// <inheritdoc cref="ISystemBDirectFeeCalculator"/>
public sealed class SystemBDirectFeeCalculator : ISystemBDirectFeeCalculator
{
    public EntitlementAmount Calculate(decimal fee)
        => new(Math.Round(fee, 2, MidpointRounding.AwayFromZero), CeilingApplied: null);
}
