using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Contracts;
using VetSystem.Domain.Common;
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
        // M29 — per-contract medication pricing was removed; the sale price is always the catalog
        // selling price. Negotiated med-price overrides now happen only at batch settlement
        // (M24's batch_settlement_lines). The signature still threads customerId/asOf so the callers
        // (field-invoice issuance, settlement preview) stay unchanged, but neither affects the result:
        // IsContractPrice is always false.
        var sellingPrice = await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => (decimal?)p.SellingPrice)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("product", productId);

        return new ResolvedUnitPrice(sellingPrice, IsContractPrice: false, ContractId: null);
    }
}
