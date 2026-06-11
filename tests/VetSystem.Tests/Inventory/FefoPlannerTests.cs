using FluentAssertions;
using VetSystem.Application.Inventory;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M25 task 2 — pure-function coverage of the FEFO selection engine: ordering (earliest expiry first,
/// no-expiry last, deterministic tie-breaks), weighted-average COGS, partial draws, multi-lot splits,
/// and the fallback-priced shortfall when lots have drifted below the materialized balance.
/// </summary>
public sealed class FefoPlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static FefoLotView Lot(
        decimal remaining, decimal cost, DateOnly? expiry, int receivedOffsetMinutes = 0, Guid? id = null)
        => new(id ?? Guid.NewGuid(), remaining, cost, expiry, T0.AddMinutes(receivedOffsetMinutes));

    [Fact]
    public void Consumes_EarliestExpiry_First()
    {
        var early = Lot(remaining: 10m, cost: 12m, expiry: new DateOnly(2026, 3, 1));
        var late = Lot(remaining: 10m, cost: 9m, expiry: new DateOnly(2026, 9, 1));

        // Pass the later-expiry lot first to prove the planner sorts, not preserves input order.
        var plan = FefoPlanner.Plan([late, early], quantity: 4m, fallbackUnitCost: 0m);

        plan.Draws.Should().HaveCount(1);
        plan.Draws[0].LotId.Should().Be(early.LotId, "the earliest-expiry lot is consumed first");
        plan.Draws[0].Quantity.Should().Be(4m);
        plan.Shortfall.Should().Be(0m);
        plan.WeightedAverageUnitCost.Should().Be(12m);
        plan.SingleLotId.Should().Be(early.LotId);
    }

    [Fact]
    public void NoExpiry_Lots_Sort_AfterDatedLots()
    {
        var dated = Lot(remaining: 3m, cost: 10m, expiry: new DateOnly(2026, 5, 1));
        var neverExpires = Lot(remaining: 100m, cost: 4m, expiry: null);

        var plan = FefoPlanner.Plan([neverExpires, dated], quantity: 2m, fallbackUnitCost: 0m);

        plan.Draws.Should().ContainSingle();
        plan.Draws[0].LotId.Should().Be(dated.LotId, "dated stock leaves before never-expiring stock");
    }

    [Fact]
    public void WeightedAverage_SpansTheConsumedLots()
    {
        var first = Lot(remaining: 3m, cost: 10m, expiry: new DateOnly(2026, 2, 1));
        var second = Lot(remaining: 5m, cost: 12m, expiry: new DateOnly(2026, 4, 1));

        var plan = FefoPlanner.Plan([first, second], quantity: 5m, fallbackUnitCost: 0m);

        plan.Draws.Should().HaveCount(2);
        plan.Draws[0].Quantity.Should().Be(3m); // all of the earlier lot
        plan.Draws[1].Quantity.Should().Be(2m); // remainder from the later lot
        plan.Shortfall.Should().Be(0m);
        // (3×10 + 2×12) / 5 = 54 / 5
        plan.WeightedAverageUnitCost.Should().Be(10.8m);
        plan.SingleLotId.Should().BeNull("a draw split across two lots leaves the movement lot_id null");
    }

    [Fact]
    public void PartialDraw_FromOneLot_LeavesRemainder_AndKeepsSingleLotId()
    {
        var lot = Lot(remaining: 10m, cost: 7.5m, expiry: new DateOnly(2026, 6, 1));

        var plan = FefoPlanner.Plan([lot], quantity: 4m, fallbackUnitCost: 0m);

        plan.Draws.Should().ContainSingle();
        plan.Draws[0].Quantity.Should().Be(4m);
        plan.WeightedAverageUnitCost.Should().Be(7.5m);
        plan.SingleLotId.Should().Be(lot.LotId);
    }

    [Fact]
    public void Shortfall_IsPricedAtFallback_AndCarriesTheFullQuantity()
    {
        var lot = Lot(remaining: 3m, cost: 10m, expiry: new DateOnly(2026, 2, 1));

        // Only 3 on hand in lots but 5 requested — the 2-unit gap is priced at the fallback cost.
        var plan = FefoPlanner.Plan([lot], quantity: 5m, fallbackUnitCost: 20m);

        plan.Draws.Should().ContainSingle();
        plan.Draws[0].Quantity.Should().Be(3m);
        plan.Shortfall.Should().Be(2m);
        // (3×10 + 2×20) / 5 = 70 / 5
        plan.WeightedAverageUnitCost.Should().Be(14m);
        plan.SingleLotId.Should().BeNull("a fallback-covered draw is not a clean single-lot deduction");
    }

    [Fact]
    public void NoLots_FallsBackEntirely_ToFallbackCost()
    {
        var plan = FefoPlanner.Plan([], quantity: 5m, fallbackUnitCost: 9m);

        plan.Draws.Should().BeEmpty();
        plan.Shortfall.Should().Be(5m);
        plan.WeightedAverageUnitCost.Should().Be(9m, "with no lots the COGS basis is the catalog fallback");
        plan.SingleLotId.Should().BeNull();
    }

    [Fact]
    public void SameExpiry_TieBreaksByReceiptThenId()
    {
        var expiry = new DateOnly(2026, 5, 1);
        var older = Lot(remaining: 2m, cost: 10m, expiry: expiry, receivedOffsetMinutes: 0);
        var newer = Lot(remaining: 2m, cost: 99m, expiry: expiry, receivedOffsetMinutes: 60);

        var plan = FefoPlanner.Plan([newer, older], quantity: 2m, fallbackUnitCost: 0m);

        plan.Draws.Should().ContainSingle();
        plan.Draws[0].LotId.Should().Be(older.LotId, "on an expiry tie the oldest receipt is consumed first (FIFO)");
    }

    [Fact]
    public void ZeroQuantity_PlansNothing()
    {
        var lot = Lot(remaining: 10m, cost: 7m, expiry: new DateOnly(2026, 6, 1));

        var plan = FefoPlanner.Plan([lot], quantity: 0m, fallbackUnitCost: 5m);

        plan.Draws.Should().BeEmpty();
        plan.Shortfall.Should().Be(0m);
    }
}
