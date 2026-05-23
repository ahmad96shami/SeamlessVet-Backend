using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Partnership;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Partnership;

/// <inheritdoc cref="IProfitDistributionService"/>
public sealed class ProfitDistributionService : IProfitDistributionService
{
    private readonly ApplicationDbContext _db;

    public ProfitDistributionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PartnerShare>> ResolveSharesAsync(
        Guid environmentId, DateOnly asOf, CancellationToken cancellationToken)
    {
        // Both queries carry the global env filter; the explicit environment_id match keeps the result
        // honest if a report calls this for a specific env, and never crosses environments (a mismatch
        // simply yields no rows — the safe isolation outcome).
        return await (
            from share in _db.PartnershipShares.AsNoTracking()
            join partner in _db.Partners.AsNoTracking() on share.PartnerId equals partner.Id
            where share.EnvironmentId == environmentId
                && share.EffectiveFrom <= asOf
                && (share.EffectiveTo == null || share.EffectiveTo >= asOf)
            orderby partner.DisplayName, partner.Id
            select new PartnerShare(partner.Id, partner.DisplayName, share.SharePercent))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProfitDistribution> DistributeAsync(
        decimal amount, Guid environmentId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var shares = await ResolveSharesAsync(environmentId, asOf, cancellationToken);
        var allocations = ProfitSplit.Distribute(amount, shares);
        var distributed = allocations.Sum(a => a.Amount);

        return new ProfitDistribution(amount, distributed, amount - distributed, allocations);
    }
}
