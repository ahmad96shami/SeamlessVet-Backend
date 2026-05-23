using FluentAssertions;
using VetSystem.Application.Partnership;

namespace VetSystem.Tests.Partnership;

/// <summary>
/// M10 task 9/11 (unit) — cent-correct profit splitting (<see cref="ProfitSplit"/>). Money path, so it
/// is exhaustively unit-tested: exact splits, rounding residuals, sub-100% totals, and the empty case.
/// </summary>
public sealed class ProfitSplitTests
{
    private static PartnerShare Share(decimal pct, string name = "P") => new(Guid.NewGuid(), name, pct);

    [Fact]
    public void FortyThirtyFiveTwentyFive_Of1000_SplitsExactly()
    {
        var allocations = ProfitSplit.Distribute(1000m, [Share(40m, "A"), Share(35m, "B"), Share(25m, "C")]);

        allocations.Select(a => a.Amount).Should().Equal(400m, 350m, 250m);
        allocations.Sum(a => a.Amount).Should().Be(1000m);
    }

    [Fact]
    public void RoundingResidual_IsDistributed_SoTotalIsExact()
    {
        // 1/3 each of 100.00 → 33.33 + 33.33 + 33.34, never 99.99.
        var allocations = ProfitSplit.Distribute(
            100m, [Share(33.33m, "A"), Share(33.33m, "B"), Share(33.34m, "C")]);

        allocations.Sum(a => a.Amount).Should().Be(100m);
        allocations.Select(a => a.Amount).Should().OnlyContain(a => a == 33.33m || a == 33.34m);
    }

    [Fact]
    public void EqualHalves_OfOddCents_LandWithinAOneCentSpread()
    {
        var allocations = ProfitSplit.Distribute(99.99m, [Share(50m, "A"), Share(50m, "B")]);

        allocations.Sum(a => a.Amount).Should().Be(99.99m);
        allocations.Select(a => a.Amount).OrderBy(a => a).Should().Equal(49.99m, 50.00m);
    }

    [Fact]
    public void SharesUnderOneHundred_LeaveTheRemainderUndistributed()
    {
        // 40 + 35 = 75% of 1000 distributed; the other 25% (250) is the clinic's retained portion.
        var allocations = ProfitSplit.Distribute(1000m, [Share(40m, "A"), Share(35m, "B")]);

        allocations.Sum(a => a.Amount).Should().Be(750m);
    }

    [Fact]
    public void NoShares_DistributesNothing()
    {
        ProfitSplit.Distribute(1000m, []).Should().BeEmpty();
    }

    [Fact]
    public void ZeroAmount_AllocatesZeroToEach()
    {
        var allocations = ProfitSplit.Distribute(0m, [Share(40m), Share(60m)]);
        allocations.Should().OnlyContain(a => a.Amount == 0m);
    }
}
