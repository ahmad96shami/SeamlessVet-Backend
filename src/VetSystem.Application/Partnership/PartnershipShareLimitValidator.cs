using VetSystem.Domain.Common;

namespace VetSystem.Application.Partnership;

/// <inheritdoc cref="IPartnershipValidator"/>
public sealed class PartnershipShareLimitValidator : IPartnershipValidator
{
    public void EnsureWithinLimit(IReadOnlyCollection<ShareWindow> shares)
    {
        // The summed share is piecewise-constant over time: it only RISES when a window starts
        // (an EffectiveFrom) and only FALLS the day after one ends. So its maximum over all dates is
        // reached at some window's EffectiveFrom — checking the active total at each distinct start
        // date is both necessary and sufficient to catch any over-allocation.
        foreach (var boundary in shares.Select(s => s.EffectiveFrom).Distinct())
        {
            var total = shares.Where(s => IsActiveOn(s, boundary)).Sum(s => s.SharePercent);
            if (total > 100m)
            {
                throw new ConflictException(
                    "partnership_overallocated",
                    $"Active partnership shares total {total:0.##}% on {boundary:yyyy-MM-dd}, which exceeds 100%.");
            }
        }
    }

    private static bool IsActiveOn(ShareWindow window, DateOnly asOf) =>
        window.EffectiveFrom <= asOf && (window.EffectiveTo is null || window.EffectiveTo >= asOf);
}
