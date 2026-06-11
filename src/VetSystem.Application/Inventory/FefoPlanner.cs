namespace VetSystem.Application.Inventory;

/// <summary>
/// A candidate lot for FEFO consumption — a pure projection of <c>InventoryLot</c> with no EF
/// dependency, so the selection math is unit-tested without a database.
/// </summary>
public sealed record FefoLotView(
    Guid LotId,
    decimal RemainingQty,
    decimal UnitCost,
    DateOnly? ExpirationDate,
    DateTimeOffset ReceivedAt);

/// <summary>How much was drawn from one lot, with its cost + expiry — used for COGS and for mirroring
/// the source lots' basis onto the destination of a transfer.</summary>
public sealed record FefoDraw(
    Guid LotId,
    decimal Quantity,
    decimal UnitCost,
    DateOnly? ExpirationDate);

/// <summary>
/// The result of planning a FEFO consumption of a quantity against a location's lots.
/// <see cref="Shortfall"/> is the quantity the lots could not cover (&gt; 0 only when the lot ledger
/// has drifted below <c>stock_items.quantity</c> — e.g. stock that pre-dates lot tracking); it is
/// priced at <see cref="FallbackUnitCost"/> so <see cref="WeightedAverageUnitCost"/> always covers
/// the full requested quantity. In the normal, fully-covered case <see cref="Shortfall"/> is 0 and
/// the weighted average is purely the consumed lots'.
/// </summary>
public sealed record FefoConsumptionPlan(
    IReadOnlyList<FefoDraw> Draws,
    decimal Shortfall,
    decimal FallbackUnitCost,
    decimal WeightedAverageUnitCost)
{
    /// <summary>The single lot a consuming movement row should reference, or <c>null</c> when the
    /// draw split across multiple lots (or fell back) — the <c>inventory_movements.lot_id</c> rule.</summary>
    public Guid? SingleLotId => Draws.Count == 1 && Shortfall == 0m ? Draws[0].LotId : null;
}

/// <summary>
/// M25 — the pure FEFO (First-Expiry-First-Out) selection engine. Given a location's on-hand lots
/// and a quantity to consume, it picks earliest-expiry lots first (lots with no expiry sort last —
/// dated stock is consumed before never-expiring stock), tie-breaking by receipt time then lot id
/// for determinism, and returns the per-lot draws plus the weighted-average unit cost the caller
/// snapshots as COGS. No I/O — the consuming service applies the plan against tracked entities.
/// </summary>
public static class FefoPlanner
{
    public static FefoConsumptionPlan Plan(
        IReadOnlyList<FefoLotView> lots,
        decimal quantity,
        decimal fallbackUnitCost)
    {
        if (quantity <= 0m)
        {
            return new FefoConsumptionPlan([], 0m, fallbackUnitCost, fallbackUnitCost);
        }

        var ordered = lots
            .Where(l => l.RemainingQty > 0m)
            .OrderBy(l => l.ExpirationDate.HasValue ? 0 : 1)         // dated lots before never-expiring
            .ThenBy(l => l.ExpirationDate ?? DateOnly.MaxValue)      // earliest expiry first
            .ThenBy(l => l.ReceivedAt)                               // then oldest receipt (FIFO)
            .ThenBy(l => l.LotId);                                   // stable final tie-break

        var draws = new List<FefoDraw>();
        var remaining = quantity;
        foreach (var lot in ordered)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(lot.RemainingQty, remaining);
            draws.Add(new FefoDraw(lot.LotId, take, lot.UnitCost, lot.ExpirationDate));
            remaining -= take;
        }

        var shortfall = remaining > 0m ? remaining : 0m;

        // Weighted-average over the lots drawn plus any uncovered remainder at the fallback cost, so
        // the average always spans the full requested quantity.
        var costSum = draws.Sum(d => d.Quantity * d.UnitCost) + (shortfall * fallbackUnitCost);
        var weighted = costSum / quantity;

        return new FefoConsumptionPlan(draws, shortfall, fallbackUnitCost, weighted);
    }
}
