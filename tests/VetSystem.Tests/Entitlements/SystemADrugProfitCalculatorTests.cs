using FluentAssertions;
using VetSystem.Application.Entitlements;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Entitlements;

/// <summary>
/// M9 task 17 + SCHEMA invariant #8 — System A drug profit across the full Cartesian product
/// {4 fee models} × {ceiling hit / not hit} × {entitlement enabled / disabled}, plus boundary cases.
/// Composes the real pure pieces (toggle resolver → exam-fee factory → System A calculator) exactly
/// as <c>EntitlementService.ComputeForBatch</c> will, asserting against hand-computed literals.
///
/// <para>Fixtures: one product line sale 100 / cost 60 / qty 10 ⇒ total drug profit 400; doctor
/// share 50%. The exam fee is deducted before the percentage. When the toggle is disabled all profit
/// goes to the clinic (computed 0). When the ceiling (100) binds, computed equals the cap.</para>
/// </summary>
public sealed class SystemADrugProfitCalculatorTests
{
    private static readonly IReadOnlyList<DrugProfitLine> Profit400 = [new DrugProfitLine(100m, 60m, 10m)];
    private const decimal Percent = 50m;

    private readonly ISystemADrugProfitCalculator _systemA = new SystemADrugProfitCalculator();
    private readonly IEntitlementToggleResolver _toggle = new EntitlementToggleResolver();
    private readonly IExamFeeCalculatorFactory _fees = new ExamFeeCalculatorFactory(
    [
        new FixedAmountExamFeeCalculator(),
        new PercentOfInvoiceExamFeeCalculator(),
        new PerBirdExamFeeCalculator(),
        new PerBatchFixedExamFeeCalculator(),
    ]);

    public sealed record Cell(
        string Model, decimal FeeValue, int Animals, decimal Invoice,
        bool Enabled, decimal? Ceiling, decimal ExpectedComputed, decimal? ExpectedCeilingApplied);

    // Exam fee per model: fixed 50, percent 12%×1000=120, per_bird 3×10=30, per_batch 80.
    // raw share = 50% × (400 − fee): fixed 175, percent 140, per_bird 185, per_batch 160.
    public static TheoryData<Cell> Matrix => new()
    {
        // fixed_amount (fee 50, raw 175)
        new(FeeModel.FixedAmount, 50m, 0, 0m, Enabled: true,  Ceiling: null, 175m, null),
        new(FeeModel.FixedAmount, 50m, 0, 0m, Enabled: true,  Ceiling: 100m, 100m, 100m),
        new(FeeModel.FixedAmount, 50m, 0, 0m, Enabled: false, Ceiling: null, 0m, null),
        new(FeeModel.FixedAmount, 50m, 0, 0m, Enabled: false, Ceiling: 100m, 0m, null),

        // percent_of_invoice (12% × 1000 = 120, raw 140)
        new(FeeModel.PercentOfInvoice, 12m, 0, 1000m, Enabled: true,  Ceiling: null, 140m, null),
        new(FeeModel.PercentOfInvoice, 12m, 0, 1000m, Enabled: true,  Ceiling: 100m, 100m, 100m),
        new(FeeModel.PercentOfInvoice, 12m, 0, 1000m, Enabled: false, Ceiling: null, 0m, null),
        new(FeeModel.PercentOfInvoice, 12m, 0, 1000m, Enabled: false, Ceiling: 100m, 0m, null),

        // per_bird (3 × 10 = 30, raw 185)
        new(FeeModel.PerBird, 3m, 10, 0m, Enabled: true,  Ceiling: null, 185m, null),
        new(FeeModel.PerBird, 3m, 10, 0m, Enabled: true,  Ceiling: 100m, 100m, 100m),
        new(FeeModel.PerBird, 3m, 10, 0m, Enabled: false, Ceiling: null, 0m, null),
        new(FeeModel.PerBird, 3m, 10, 0m, Enabled: false, Ceiling: 100m, 0m, null),

        // per_batch_fixed (fee 80, raw 160)
        new(FeeModel.PerBatchFixed, 80m, 0, 0m, Enabled: true,  Ceiling: null, 160m, null),
        new(FeeModel.PerBatchFixed, 80m, 0, 0m, Enabled: true,  Ceiling: 100m, 100m, 100m),
        new(FeeModel.PerBatchFixed, 80m, 0, 0m, Enabled: false, Ceiling: null, 0m, null),
        new(FeeModel.PerBatchFixed, 80m, 0, 0m, Enabled: false, Ceiling: 100m, 0m, null),
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void SystemA_AcrossFeeModelCeilingAndToggle(Cell c)
    {
        // Toggle: a null per-batch override falls through to the global default (invariant #4).
        var enabled = _toggle.IsEnabled(perBatchOverride: null, globalDefault: c.Enabled);

        var examFee = _fees.For(c.Model).Calculate(new ExamFeeBasis(c.FeeValue, c.Animals, c.Invoice));

        var result = enabled
            ? _systemA.Calculate(new SystemAInput(Profit400, examFee, Percent, c.Ceiling))
            : new EntitlementAmount(0m, null); // disabled → all profit to the clinic

        result.ComputedAmount.Should().Be(c.ExpectedComputed);
        result.CeilingApplied.Should().Be(c.ExpectedCeilingApplied);
    }

    [Fact]
    public void ExamFeeExceedingProfit_ClampsToZero()
    {
        // profit 400, exam fee 500 → basis −100 → raw −50 → clamped to 0 (clinic absorbs).
        var result = _systemA.Calculate(new SystemAInput(Profit400, ExamFee: 500m, DoctorSharePercent: 50m, DoctorShareCeiling: null));
        result.ComputedAmount.Should().Be(0m);
        result.CeilingApplied.Should().BeNull();
    }

    [Fact]
    public void ZeroProfit_YieldsZero()
    {
        var result = _systemA.Calculate(new SystemAInput([], ExamFee: 0m, DoctorSharePercent: 50m, DoctorShareCeiling: null));
        result.ComputedAmount.Should().Be(0m);
    }

    [Fact]
    public void HundredPercentShare_TakesAllProfitAfterExamFee()
    {
        // 100% × (400 − 100) = 300
        var result = _systemA.Calculate(new SystemAInput(Profit400, ExamFee: 100m, DoctorSharePercent: 100m, DoctorShareCeiling: null));
        result.ComputedAmount.Should().Be(300m);
        result.CeilingApplied.Should().BeNull();
    }

    [Fact]
    public void CeilingEqualToShare_IsNotTreatedAsCapped()
    {
        // raw share = 50% × (400 − 0) = 200; ceiling 200 → not "exceeded", so no cap is recorded.
        var result = _systemA.Calculate(new SystemAInput(Profit400, ExamFee: 0m, DoctorSharePercent: 50m, DoctorShareCeiling: 200m));
        result.ComputedAmount.Should().Be(200m);
        result.CeilingApplied.Should().BeNull("the ceiling only binds when the raw share strictly exceeds it");
    }

    [Fact]
    public void ContractOverriddenSaleValue_DrivesProfit_NotCatalogPrice()
    {
        // sale_value resolution (task 6): the line carries the contract price (90), not catalog. Profit
        // per unit = 90 − 60 = 30 over qty 10 = 300; 50% with no exam fee = 150.
        var lines = new List<DrugProfitLine> { new(SaleValue: 90m, Cost: 60m, Quantity: 10m) };
        var result = _systemA.Calculate(new SystemAInput(lines, ExamFee: 0m, DoctorSharePercent: 50m, DoctorShareCeiling: null));
        result.ComputedAmount.Should().Be(150m);
    }

    // ---- M24 — the batch-settlement discount enters the basis like the exam fee ----

    [Fact]
    public void SettlementDiscount_ReducesTheBasis_LikeTheExamFee()
    {
        // 50% × (400 − 100 − 50) = 125 (the worked example from the M24 client decision).
        var result = _systemA.Calculate(new SystemAInput(
            Profit400, ExamFee: 100m, DoctorSharePercent: 50m, DoctorShareCeiling: null, SettlementDiscount: 50m));
        result.ComputedAmount.Should().Be(125m);
        result.CeilingApplied.Should().BeNull();
    }

    [Fact]
    public void SettlementDiscount_ExceedingProfit_ClampsToZero()
    {
        // basis = 400 − 100 − 350 = −50 → clamped to 0 (clinic absorbs, same as the exam-fee clamp).
        var result = _systemA.Calculate(new SystemAInput(
            Profit400, ExamFee: 100m, DoctorSharePercent: 50m, DoctorShareCeiling: null, SettlementDiscount: 350m));
        result.ComputedAmount.Should().Be(0m);
        result.CeilingApplied.Should().BeNull();
    }

    [Fact]
    public void SettlementDiscount_ComposesWithTheCeiling()
    {
        // 100% × (400 − 0 − 100) = 300, ceiling 250 → capped at 250.
        var result = _systemA.Calculate(new SystemAInput(
            Profit400, ExamFee: 0m, DoctorSharePercent: 100m, DoctorShareCeiling: 250m, SettlementDiscount: 100m));
        result.ComputedAmount.Should().Be(250m);
        result.CeilingApplied.Should().Be(250m);
    }

    [Fact]
    public void DefaultDiscount_IsZero_PreservingPreM24Behaviour()
    {
        var withDefault = _systemA.Calculate(new SystemAInput(Profit400, 100m, 50m, null));
        var withExplicitZero = _systemA.Calculate(new SystemAInput(Profit400, 100m, 50m, null, SettlementDiscount: 0m));
        withDefault.Should().Be(withExplicitZero);
        withDefault.ComputedAmount.Should().Be(150m);
    }
}
