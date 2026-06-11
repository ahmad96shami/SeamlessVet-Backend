using FluentAssertions;
using VetSystem.Application.Entitlements;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Entitlements;

/// <summary>
/// M28 — the reformulated entitlement split (the money-critical heart). The supervision fee IS the
/// doctor's entitlement in both systems (no percent, no ceiling, no clamp); System A vs B is an
/// accounting-only difference. This exercises the full {System A/B} × {toggle on/off} × {discount}
/// matrix against hand-computed literals, the <c>per_bird 0.1 × 15000 = 1500</c> exit example, the
/// no-clamp negative-clinic case, and the System-B fee-on-top.
///
/// <para>Fixtures: drug profit 1000, supervision fee 200. The single identity under test is
/// <c>clinicShare = drugProfit + feeAddedToSettlement − discount − doctorShare</c>.</para>
/// </summary>
public sealed class EntitlementSplitCalculatorTests
{
    private const decimal DrugProfit = 1000m;
    private const decimal Fee = 200m;

    public sealed record Cell(
        string System, bool Enabled, decimal Discount,
        decimal ExpectedDoctor, decimal ExpectedClinic, decimal ExpectedFeeAdded, decimal ExpectedFeeRetained);

    public static TheoryData<Cell> Matrix => new()
    {
        // System A (drug_profit): the fee is carved from the clinic's margin — never charged to the farmer.
        new(EntitlementSystem.DrugProfit, Enabled: true,  Discount: 0m,   ExpectedDoctor: 200m, ExpectedClinic: 800m,  ExpectedFeeAdded: 0m, ExpectedFeeRetained: 0m),
        new(EntitlementSystem.DrugProfit, Enabled: true,  Discount: 100m, ExpectedDoctor: 200m, ExpectedClinic: 700m,  ExpectedFeeAdded: 0m, ExpectedFeeRetained: 0m),
        new(EntitlementSystem.DrugProfit, Enabled: false, Discount: 0m,   ExpectedDoctor: 0m,   ExpectedClinic: 1000m, ExpectedFeeAdded: 0m, ExpectedFeeRetained: 0m),
        new(EntitlementSystem.DrugProfit, Enabled: false, Discount: 100m, ExpectedDoctor: 0m,   ExpectedClinic: 900m,  ExpectedFeeAdded: 0m, ExpectedFeeRetained: 0m),

        // System B (direct_fee): the farmer always pays the fee on top; the clinic keeps it when the toggle is off.
        new(EntitlementSystem.DirectFee, Enabled: true,  Discount: 0m,   ExpectedDoctor: 200m, ExpectedClinic: 1000m, ExpectedFeeAdded: 200m, ExpectedFeeRetained: 0m),
        new(EntitlementSystem.DirectFee, Enabled: true,  Discount: 100m, ExpectedDoctor: 200m, ExpectedClinic: 900m,  ExpectedFeeAdded: 200m, ExpectedFeeRetained: 0m),
        new(EntitlementSystem.DirectFee, Enabled: false, Discount: 0m,   ExpectedDoctor: 0m,   ExpectedClinic: 1200m, ExpectedFeeAdded: 200m, ExpectedFeeRetained: 200m),
        new(EntitlementSystem.DirectFee, Enabled: false, Discount: 100m, ExpectedDoctor: 0m,   ExpectedClinic: 1100m, ExpectedFeeAdded: 200m, ExpectedFeeRetained: 200m),
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Split_AcrossSystemToggleAndDiscount(Cell c)
    {
        var split = EntitlementSplitCalculator.Resolve(c.System, c.Enabled, DrugProfit, Fee, c.Discount);

        split.DoctorShare.Should().Be(c.ExpectedDoctor);
        split.ClinicShare.Should().Be(c.ExpectedClinic);
        split.FeeAddedToSettlement.Should().Be(c.ExpectedFeeAdded);
        split.FeeRetainedByClinic.Should().Be(c.ExpectedFeeRetained);

        // The unifying pie identity holds for every cell.
        split.ClinicShare.Should().Be(DrugProfit + split.FeeAddedToSettlement - c.Discount - split.DoctorShare);
    }

    [Fact]
    public void PerBird_ComputesTheSupervisionFee_AndFeedsTheSplit()
    {
        // Exit example: per-bird rate 0.1 × 15000 birds = 1500 supervision fee.
        var fee = new PerBirdExamFeeCalculator().Calculate(new ExamFeeBasis(FeeValue: 0.1m, AnimalCount: 15000, InvoiceTotal: 0m));
        fee.Should().Be(1500m);

        var split = EntitlementSplitCalculator.Resolve(
            EntitlementSystem.DrugProfit, enabled: true, drugProfit: 5000m, supervisionFee: fee, settlementDiscount: 0m);
        split.DoctorShare.Should().Be(1500m);
        split.ClinicShare.Should().Be(3500m); // 5000 − 1500
    }

    [Fact]
    public void SystemA_FeeExceedingProfit_PaysFullFee_ClinicGoesNegative_NoClamp()
    {
        // No clamp: the doctor is paid the full fee even though it exceeds drug profit; the clinic absorbs it.
        var split = EntitlementSplitCalculator.Resolve(
            EntitlementSystem.DrugProfit, enabled: true, drugProfit: 100m, supervisionFee: 500m, settlementDiscount: 0m);

        split.DoctorShare.Should().Be(500m, "the full fee is paid with no clamp");
        split.ClinicShare.Should().Be(-400m, "100 profit − 500 fee");
        split.FeeAddedToSettlement.Should().Be(0m);
    }

    [Fact]
    public void SystemB_FeeOnTop_IsAWashForTheClinic_WhenEnabled()
    {
        // The farmer funds the fee (FeeAddedToSettlement) and it passes through to the doctor, so the
        // clinic keeps exactly the drug profit.
        var split = EntitlementSplitCalculator.Resolve(
            EntitlementSystem.DirectFee, enabled: true, drugProfit: 1000m, supervisionFee: 200m, settlementDiscount: 0m);

        split.FeeAddedToSettlement.Should().Be(200m);
        split.DoctorShare.Should().Be(200m);
        split.ClinicShare.Should().Be(1000m);
    }

    [Fact]
    public void Fee_IsRoundedToTheCent()
    {
        var split = EntitlementSplitCalculator.Resolve(
            EntitlementSystem.DrugProfit, enabled: true, drugProfit: 1000m, supervisionFee: 199.999m, settlementDiscount: 0m);
        split.DoctorShare.Should().Be(200.00m);
    }
}
