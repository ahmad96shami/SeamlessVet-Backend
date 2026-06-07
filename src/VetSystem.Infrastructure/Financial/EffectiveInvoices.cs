using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Financial;

/// <summary>
/// The shared "effective invoices" loader (extracted in M24): a source's invoices net of voids — a
/// voided original (status still <c>issued</c>, append-only) is dropped along with its <c>void</c>
/// reversal row, so neither its totals nor its line items count. Entitlement compute, batch
/// settlement, and the settlement-aware reports all resolve "which rows count" through this one
/// definition so their figures reconcile to the cent.
/// </summary>
public static class EffectiveInvoices
{
    /// <summary>Returns the surviving invoices keyed by id, plus their product line items
    /// (<c>product_id IS NOT NULL</c> — service lines never enter drug-profit/settlement math).</summary>
    public static async Task<(Dictionary<Guid, Invoice> Invoices, List<InvoiceItem> ProductItems)> LoadAsync(
        ApplicationDbContext db,
        Expression<Func<Invoice, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var all = await db.Invoices.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);

        var voidedOriginalIds = all.Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();

        var effective = all
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .ToDictionary(i => i.Id);

        if (effective.Count == 0)
        {
            return (effective, []);
        }

        var ids = effective.Keys.ToList();
        var productItems = await db.InvoiceItems.AsNoTracking()
            .Where(it => ids.Contains(it.InvoiceId) && it.ProductId != null)
            .ToListAsync(cancellationToken);

        return (effective, productItems);
    }
}
