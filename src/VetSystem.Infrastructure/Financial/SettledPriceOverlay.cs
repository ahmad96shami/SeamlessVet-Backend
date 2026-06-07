using Microsoft.EntityFrameworkCore;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Financial;

/// <summary>
/// M24 — the reporting side of batch settlement (SCHEMA invariant #11). A settled batch's product
/// lines are worth their <em>negotiated</em> price, not the billed one, and the effect is
/// <b>retroactive</b> (like voids): re-running a past window after a settlement shows the settled
/// numbers. The batch-level discount is the exception — it has no per-line attribution, so it lands
/// in clinic-profit/P&amp;L at <c>settled_at</c> only (never in per-product / per-visit / per-doctor
/// slices).
///
/// <para><b>Rounding caveat:</b> the ledger adjustment posted at settle time rounds once per product
/// (<c>Σ Money(δ_product)</c>); the per-line overlay here is unrounded and summed per invoice, so a
/// window total can differ from the ledger by sub-cent residues when a product's delta doesn't round
/// cleanly. That residue is bounded by half a cent per product and is the standard cost of
/// attributing a batch-level negotiation to individual invoices.</para>
/// </summary>
public static class SettledPriceOverlay
{
    /// <summary>Settled price maps for the given batches: <c>batchId → (productId → settled price)</c>.
    /// Batches without a settlement simply have no entry.</summary>
    public static async Task<Dictionary<Guid, Dictionary<Guid, decimal>>> LoadPriceMapsAsync(
        ApplicationDbContext db, IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken)
    {
        if (batchIds.Count == 0)
        {
            return [];
        }

        var rows = await (
                from s in db.BatchSettlements.AsNoTracking()
                join l in db.BatchSettlementLines.AsNoTracking() on s.Id equals l.SettlementId
                where batchIds.Contains(s.BatchId)
                select new { s.BatchId, l.ProductId, l.SettledUnitPrice })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.BatchId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.ProductId, r => r.SettledUnitPrice));
    }

    /// <summary>One line's repricing delta: <c>(settled − billed unit price) × qty</c>. Per-line
    /// discounts cancel out of the delta (they apply identically before and after).</summary>
    public static decimal LineDelta(decimal settledUnitPrice, decimal unitPrice, decimal quantity)
        => (settledUnitPrice - unitPrice) * quantity;

    /// <summary>One line's overlaid revenue: <c>settled × qty − line discount</c> (the settled
    /// counterpart of the stored <c>line_total = unit × qty − discount</c>).</summary>
    public static decimal OverlaidLineRevenue(decimal settledUnitPrice, decimal quantity, decimal discountAmount)
        => settledUnitPrice * quantity - discountAmount;

    /// <summary>
    /// Per-invoice repricing deltas for a set of effective invoices: loads the product lines of the
    /// invoices whose batch is settled, applies the price maps, and returns
    /// <c>invoiceId → Σ line deltas</c> (absent ⇒ 0). Pass every effective (invoice, batch) pair of
    /// the window — unsettled batches are filtered here so callers stay oblivious.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> LoadInvoiceDeltasAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<(Guid InvoiceId, Guid BatchId)> batchInvoices,
        CancellationToken cancellationToken)
    {
        if (batchInvoices.Count == 0)
        {
            return [];
        }

        var priceMaps = await LoadPriceMapsAsync(
            db, batchInvoices.Select(x => x.BatchId).Distinct().ToList(), cancellationToken);
        if (priceMaps.Count == 0)
        {
            return [];
        }

        var settledInvoiceIds = batchInvoices
            .Where(x => priceMaps.ContainsKey(x.BatchId))
            .Select(x => x.InvoiceId)
            .ToList();
        var batchByInvoice = batchInvoices.ToDictionary(x => x.InvoiceId, x => x.BatchId);

        var items = await db.InvoiceItems.AsNoTracking()
            .Where(it => settledInvoiceIds.Contains(it.InvoiceId) && it.ProductId != null)
            .Select(it => new { it.InvoiceId, ProductId = it.ProductId!.Value, it.UnitPrice, it.Quantity })
            .ToListAsync(cancellationToken);

        var deltas = new Dictionary<Guid, decimal>();
        foreach (var item in items)
        {
            if (priceMaps[batchByInvoice[item.InvoiceId]].TryGetValue(item.ProductId, out var settled))
            {
                deltas[item.InvoiceId] = deltas.GetValueOrDefault(item.InvoiceId)
                    + LineDelta(settled, item.UnitPrice, item.Quantity);
            }
        }

        return deltas;
    }

    /// <summary>Σ settlement discounts granted in the window (attributed at <c>settled_at</c> —
    /// the discount is a settlement-day event, not a per-invoice one).</summary>
    public static async Task<decimal> SumDiscountsSettledInWindowAsync(
        ApplicationDbContext db, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
        => await db.BatchSettlements.AsNoTracking()
               .Where(s => s.SettledAt >= start && s.SettledAt < end)
               .SumAsync(s => (decimal?)s.DiscountAmount, cancellationToken)
           ?? 0m;
}
