using FluentAssertions;
using VetSystem.Application.Entitlements;

namespace VetSystem.Tests.Entitlements;

/// <summary>
/// M9 task 18 (calculator half) — System B direct fee credits the full exam/supervision fee to the
/// doctor, with no ceiling (PRD §7.4). Plus the toggle resolver (task 4): a per-batch override beats
/// the global default; null inherits it. The end-to-end standalone exam-fee invoice flow is exercised
/// by the integration suite.
/// </summary>
public sealed class SystemBAndToggleTests
{
    private readonly ISystemBDirectFeeCalculator _systemB = new SystemBDirectFeeCalculator();
    private readonly IEntitlementToggleResolver _toggle = new EntitlementToggleResolver();

    [Theory]
    [InlineData(500, 500)]
    [InlineData(0, 0)]
    public void SystemB_CreditsTheFullFee(decimal fee, decimal expected)
    {
        var result = _systemB.Calculate(fee);
        result.ComputedAmount.Should().Be(expected);
        result.CeilingApplied.Should().BeNull("System B has no ceiling — the agreed fee is credited in full");
    }

    [Fact]
    public void SystemB_RoundsToTheCent()
    {
        _systemB.Calculate(199.999m).ComputedAmount.Should().Be(200.00m);
    }

    [Theory]
    [InlineData(null, true, true)]    // null override → inherit global (enabled)
    [InlineData(null, false, false)]  // null override → inherit global (disabled)
    [InlineData(true, false, true)]   // per-batch opt-in overrides a globally-disabled system
    [InlineData(false, true, false)]  // per-batch opt-out overrides a globally-enabled system
    public void Toggle_PerBatchOverrideBeatsGlobal(bool? perBatch, bool global, bool expected)
    {
        _toggle.IsEnabled(perBatch, global).Should().Be(expected);
    }
}
