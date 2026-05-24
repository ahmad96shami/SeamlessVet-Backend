using CsCheck;
using FluentAssertions;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M13 task 2 — property-based (CsCheck) coverage of the inventory delta math: the pure
/// <see cref="MovementTranslator"/> that turns a high-level intent into signed legs (M4). The
/// materialized <c>stock_items.quantity == Σ quantity_delta</c> invariant holds by construction only
/// if these legs are right: transfers conserve stock (legs sum to zero across two distinct locations),
/// single-location moves carry the correctly-signed quantity, and every leg's <i>affected</i> location
/// follows the sign rule (to-side when ≥ 0, from-side when &lt; 0).
/// </summary>
public sealed class MovementTranslatorPropertyTests
{
    private static readonly MovementTranslator Translator = new();

    // Integer hundredths ÷ 100 — exact 2-decimal quantities in [0.01, 5,000.00], and avoids CsCheck's
    // Gen.Decimal[min,max] double→decimal overflow.
    private static readonly Gen<decimal> PositiveQuantity = Gen.Int[1, 500_000].Select(h => h / 100m);

    [Fact]
    public void Transfers_ConserveStock_AcrossTwoDistinctLocations()
    {
        var transferTypes = Gen.OneOfConst(MovementType.LoadToField, MovementType.UnloadFromField);

        Gen.Select(transferTypes, PositiveQuantity).Sample((type, quantity) =>
        {
            var legs = Translator.Translate(BuildTransfer(type, quantity));

            legs.Should().HaveCount(2);
            legs.Sum(l => l.SignedDelta).Should().Be(0m, "a transfer neither creates nor destroys stock");
            legs.Should().OnlyContain(l => Math.Abs(l.SignedDelta) == quantity);
            legs.Select(l => l.AffectedLocationId).Distinct().Should().HaveCount(2, "debit + credit hit different locations");
            foreach (var leg in legs)
            {
                AssertAffectedMatchesSign(leg);
            }
        });
    }

    [Fact]
    public void SingleLegMovements_CarryTheExpectedSignedDelta()
    {
        var singleTypes = Gen.OneOfConst(MovementType.Receive, MovementType.ReturnAdd, MovementType.SaleDeduct);

        Gen.Select(singleTypes, PositiveQuantity).Sample((type, quantity) =>
        {
            var legs = Translator.Translate(BuildSingle(type, quantity));

            legs.Should().HaveCount(1);
            var expected = type == MovementType.SaleDeduct ? -quantity : quantity;
            legs[0].SignedDelta.Should().Be(expected);
            AssertAffectedMatchesSign(legs[0]);
        });
    }

    [Fact]
    public void Adjust_LegCarriesTheSignedDelta_ForEitherSign()
    {
        Gen.Int[-500_000, 500_000]
            .Where(h => h != 0)
            .Select(h => h / 100m)
            .Sample(delta =>
            {
                var intent = new MovementIntent(
                    null, MovementType.Adjust, Guid.NewGuid(), delta,
                    null, null, StockLocation.Warehouse, Guid.NewGuid(), "k");

                var legs = Translator.Translate(intent);

                legs.Should().HaveCount(1);
                legs[0].SignedDelta.Should().Be(delta);
                AssertAffectedMatchesSign(legs[0]);
            });
    }

    private static void AssertAffectedMatchesSign(PlannedMovementLeg leg)
    {
        if (leg.SignedDelta >= 0m)
        {
            leg.AffectedLocationId.Should().Be(leg.ToLocationId!.Value);
        }
        else
        {
            leg.AffectedLocationId.Should().Be(leg.FromLocationId!.Value);
        }
    }

    private static MovementIntent BuildTransfer(string type, decimal quantity) => type == MovementType.LoadToField
        ? new MovementIntent(null, type, Guid.NewGuid(), quantity,
            StockLocation.Warehouse, Guid.NewGuid(), StockLocation.Field, Guid.NewGuid(), "k")
        : new MovementIntent(null, type, Guid.NewGuid(), quantity,
            StockLocation.Field, Guid.NewGuid(), StockLocation.Warehouse, Guid.NewGuid(), "k");

    private static MovementIntent BuildSingle(string type, decimal quantity) => type switch
    {
        MovementType.SaleDeduct => new MovementIntent(null, type, Guid.NewGuid(), quantity,
            StockLocation.Warehouse, Guid.NewGuid(), null, null, "k"),
        _ => new MovementIntent(null, type, Guid.NewGuid(), quantity,
            null, null, StockLocation.Warehouse, Guid.NewGuid(), "k"),
    };
}
