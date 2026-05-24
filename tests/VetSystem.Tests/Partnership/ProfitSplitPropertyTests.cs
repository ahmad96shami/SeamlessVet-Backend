using CsCheck;
using FluentAssertions;
using VetSystem.Application.Partnership;

namespace VetSystem.Tests.Partnership;

/// <summary>
/// M13 task 2 — property-based (CsCheck) coverage of the cent-correct partnership split (PRD §6.8).
/// Over many generated (amount, share-percent) cases the largest-remainder distribution must preserve
/// the partner count, stay non-negative, sum <b>cent-exactly</b> to <c>round(amount × Σpct ÷ 100)</c>,
/// and give each partner an amount within one cent of their exact proportional cut. Complements the
/// example-based <c>ProfitSplitTests</c> with generated + shrinking inputs.
/// </summary>
public sealed class ProfitSplitPropertyTests
{
    // Generate money as integer cents ÷ 100 — exact 2-decimal values, and avoids CsCheck's
    // Gen.Decimal[min,max] double→decimal overflow. Amount ∈ [0, 100,000.00].
    private static readonly Gen<decimal> Amount = Gen.Int[0, 10_000_000].Select(cents => cents / 100m);

    // 1–5 partners, each ≤ 20.00% ⇒ Σ ≤ 100% by construction (the split's precondition).
    private static readonly Gen<List<decimal>> Percents = Gen.Int[0, 2_000].Select(bp => bp / 100m).List[1, 5];

    [Fact]
    public void Distribute_IsCentExact_Fair_NonNegative_AndPreservesCount()
    {
        Gen.Select(Amount, Percents).Sample((amount, percents) =>
        {
            var shares = percents
                .Select((pct, i) => new PartnerShare(Guid.NewGuid(), $"P{i}", pct))
                .ToList();

            var allocations = ProfitSplit.Distribute(amount, shares);

            allocations.Should().HaveCount(shares.Count);
            allocations.Should().OnlyContain(a => a.Amount >= 0m);

            // Cent-exact: the distributed total equals round(amount × Σpct ÷ 100) to the cent.
            var sumPct = percents.Sum();
            var amountCents = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
            var targetCents = decimal.Round(amountCents * sumPct / 100m, 0, MidpointRounding.AwayFromZero);
            var actualCents = decimal.Round(allocations.Sum(a => a.Amount) * 100m, 0, MidpointRounding.AwayFromZero);
            actualCents.Should().Be(targetCents, "the distributed total must be cent-exact");

            // Fair: each partner is within one cent of their exact proportional cut.
            foreach (var allocation in allocations)
            {
                var ideal = amount * allocation.SharePercent / 100m;
                Math.Abs(allocation.Amount - ideal).Should().BeLessThanOrEqualTo(0.01m,
                    "largest-remainder keeps each partner within a cent of their exact cut");
            }
        });
    }
}
