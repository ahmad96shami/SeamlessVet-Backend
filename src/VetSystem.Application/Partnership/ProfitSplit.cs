namespace VetSystem.Application.Partnership;

/// <summary>
/// Pure, cent-correct split of an amount across partnership shares (PRD §6.8). Kept separate from the
/// DB-backed <see cref="IProfitDistributionService"/> so the money math is unit-tested in isolation.
/// </summary>
public static class ProfitSplit
{
    /// <summary>
    /// Splits <paramref name="amount"/> across <paramref name="shares"/> by their percentages. Works in
    /// integer cents and hands out the rounding residual via the largest-remainder method, so the
    /// distributed total equals <c>round(amount × Σpct ÷ 100)</c> to the cent and each partner is within
    /// one cent of their exact proportional cut. When <c>Σpct &lt; 100</c> the shortfall stays with the
    /// clinic (reported by the caller as the retained portion).
    /// </summary>
    public static IReadOnlyList<ProfitAllocation> Distribute(decimal amount, IReadOnlyList<PartnerShare> shares)
    {
        if (shares.Count == 0)
        {
            return Array.Empty<ProfitAllocation>();
        }

        var amountCents = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        var sumPct = shares.Sum(s => s.SharePercent);
        var targetCents = decimal.Round(amountCents * sumPct / 100m, 0, MidpointRounding.AwayFromZero);

        var working = shares
            .Select(s =>
            {
                var ideal = amountCents * s.SharePercent / 100m;
                var floor = decimal.Floor(ideal);
                return (Share: s, Floor: floor, Frac: ideal - floor);
            })
            .ToList();

        var allocatedCents = working.Sum(w => w.Floor);
        var leftover = (int)(targetCents - allocatedCents); // 0 ≤ leftover ≤ shares.Count

        // Hand each leftover cent to the share with the largest fractional remainder; tie-break by the
        // larger percentage, then original order — fully deterministic.
        var bump = working
            .Select((w, index) => (index, w.Frac, w.Share.SharePercent))
            .OrderByDescending(x => x.Frac)
            .ThenByDescending(x => x.SharePercent)
            .ThenBy(x => x.index)
            .Take(Math.Max(0, leftover))
            .Select(x => x.index)
            .ToHashSet();

        return working
            .Select((w, index) =>
            {
                var cents = w.Floor + (bump.Contains(index) ? 1m : 0m);
                return new ProfitAllocation(w.Share.PartnerId, w.Share.DisplayName, w.Share.SharePercent, cents / 100m);
            })
            .ToList();
    }
}
