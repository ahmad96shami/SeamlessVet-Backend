using CsCheck;
using FluentAssertions;
using VetSystem.Infrastructure.Financial;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M24 — the pure overlay arithmetic every settlement-aware consumer leans on. The core identity is
/// algebraic and must hold <b>exactly</b> in decimal (the helpers do no rounding):
/// <c>OverlaidLineRevenue == line_total + LineDelta</c>, because
/// <c>settled×qty − disc == (unit×qty − disc) + (settled − unit)×qty</c>. Per-line discounts cancel
/// out of the delta. Generated as integer cents (the repo's CsCheck convention) so inputs are exact
/// 2-decimal money values.
/// </summary>
public sealed class SettledPriceOverlayTests
{
    // Money as integer cents ÷ 100; quantity as integer thousandths ÷ 1000 (numeric(14,3)).
    private static readonly Gen<decimal> Price = Gen.Int[0, 100_000].Select(cents => cents / 100m);
    private static readonly Gen<decimal> Quantity = Gen.Int[0, 100_000].Select(milli => milli / 1000m);

    [Fact]
    public void OverlaidRevenue_Equals_LineTotal_Plus_Delta_Exactly()
    {
        var line = Gen.Select(Price, Quantity, Price, Price,
            (unit, qty, settled, disc) => (Unit: unit, Qty: qty, Settled: settled, Disc: disc));

        line.List[1, 40].Sample(lines =>
        {
            var originalTotal = lines.Sum(l => l.Unit * l.Qty - l.Disc);
            var overlaidTotal = lines.Sum(l => SettledPriceOverlay.OverlaidLineRevenue(l.Settled, l.Qty, l.Disc));
            var deltaTotal = lines.Sum(l => SettledPriceOverlay.LineDelta(l.Settled, l.Unit, l.Qty));

            // The identity is exact — no tolerance.
            overlaidTotal.Should().Be(originalTotal + deltaTotal);
        });
    }

    [Fact]
    public void LineDelta_IsDiscountInvariant()
    {
        Gen.Select(Price, Quantity, Price, (unit, qty, settled) => (unit, qty, settled))
            .Sample(t =>
            {
                // The per-line discount appears nowhere in the delta's signature — by construction —
                // and the delta of an unchanged price is exactly zero.
                SettledPriceOverlay.LineDelta(t.settled, t.unit, t.qty)
                    .Should().Be((t.settled - t.unit) * t.qty);
                SettledPriceOverlay.LineDelta(t.unit, t.unit, t.qty).Should().Be(0m);
            });
    }

    [Fact]
    public void SettlingAtTheWeightedAverage_YieldsZeroTotalDelta()
    {
        // The preview prefills the weighted average so the screen opens delta-neutral: settling every
        // line of a product at Σ(unit×qty)/Σqty makes Σ deltas vanish (up to the prefill's 2dp
        // rounding — so assert against the unrounded average, which is the mathematical statement).
        var line = Gen.Select(Price, Gen.Int[1, 1000].Select(m => m / 10m), (unit, qty) => (Unit: unit, Qty: qty));

        line.List[1, 20].Sample(lines =>
        {
            var totalQty = lines.Sum(l => l.Qty);
            var weighted = lines.Sum(l => l.Unit * l.Qty) / totalQty;

            var totalDelta = lines.Sum(l => SettledPriceOverlay.LineDelta(weighted, l.Unit, l.Qty));
            totalDelta.Should().BeApproximately(0m, 0.0000001m);
        });
    }
}
