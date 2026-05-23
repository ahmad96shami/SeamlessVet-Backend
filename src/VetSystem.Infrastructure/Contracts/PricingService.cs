using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Contracts;

/// <inheritdoc cref="IPricingService"/>
public sealed class PricingService : IPricingService
{
    private readonly ApplicationDbContext _db;

    public PricingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ResolvedUnitPrice> ResolveUnitPriceAsync(
        Guid productId, Guid? customerId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var sellingPrice = await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => (decimal?)p.SellingPrice)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("product", productId);

        if (customerId is not { } cid)
        {
            return new ResolvedUnitPrice(sellingPrice, IsContractPrice: false, ContractId: null);
        }

        // The override applies only under an *active* contract whose period covers asOf. If more than
        // one qualifies, the most recently started contract wins (then most recently created), so the
        // result is deterministic. Both queries are environment-scoped by the global filter.
        var match = await (
            from cmp in _db.ContractMedicationPrices.AsNoTracking()
            join c in _db.Contracts.AsNoTracking() on cmp.ContractId equals c.Id
            where cmp.ProductId == productId
                && c.CustomerId == cid
                && c.Status == ContractStatus.Active
                && c.PeriodStart <= asOf
                && (c.PeriodEnd == null || c.PeriodEnd >= asOf)
            orderby c.PeriodStart descending, c.CreatedAt descending
            select new { cmp.ContractPrice, ContractId = c.Id })
            .FirstOrDefaultAsync(cancellationToken);

        return match is null
            ? new ResolvedUnitPrice(sellingPrice, IsContractPrice: false, ContractId: null)
            : new ResolvedUnitPrice(match.ContractPrice, IsContractPrice: true, match.ContractId);
    }
}
