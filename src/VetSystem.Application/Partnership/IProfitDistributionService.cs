namespace VetSystem.Application.Partnership;

/// <summary>
/// Resolves and applies partnership profit distribution for an environment (PRD §6.8, tasks 5–6).
/// <see cref="ResolveSharesAsync"/> returns the active share map on a date; <see cref="DistributeAsync"/>
/// splits a given amount (e.g. a closed batch's clinic share) across those shares, cent-correct. M12's
/// clinic-profit and per-batch reports consume this; the calculation lives here so the split is defined
/// once and tested once.
/// </summary>
public interface IProfitDistributionService
{
    /// <summary>Partners with a share active on <paramref name="asOf"/>, ordered by display name.</summary>
    Task<IReadOnlyList<PartnerShare>> ResolveSharesAsync(
        Guid environmentId, DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Splits <paramref name="amount"/> across the shares active on <paramref name="asOf"/>. Because
    /// active shares sum to ≤ 100% (the partnership invariant), any unallocated remainder is reported as
    /// <see cref="ProfitDistribution.Retained"/> (the clinic's own portion).
    /// </summary>
    Task<ProfitDistribution> DistributeAsync(
        decimal amount, Guid environmentId, DateOnly asOf, CancellationToken cancellationToken);
}

/// <summary>A partner's active share percent on the resolved date.</summary>
public sealed record PartnerShare(Guid PartnerId, string DisplayName, decimal SharePercent);

/// <summary>One partner's resolved monetary cut of a distributed amount.</summary>
public sealed record ProfitAllocation(Guid PartnerId, string DisplayName, decimal SharePercent, decimal Amount);

/// <summary>
/// The result of distributing <see cref="Amount"/>: per-partner <see cref="Allocations"/> summing to
/// <see cref="DistributedTotal"/>, plus <see cref="Retained"/> = <see cref="Amount"/> − distributed.
/// </summary>
public sealed record ProfitDistribution(
    decimal Amount,
    decimal DistributedTotal,
    decimal Retained,
    IReadOnlyList<ProfitAllocation> Allocations);
