using VetSystem.Application.Common;
using VetSystem.Application.Entitlements;
using VetSystem.Application.Partnership;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;

namespace VetSystem.API.Reports;

/// <summary>
/// Profit-per-farm-batch report (M12 task 4, PRD §7.9). The drug-profit accounting is produced by the
/// M9 engine via <see cref="IEntitlementService.ExplainForBatchAsync"/> — the <em>same</em> inputs that
/// produce the persisted entitlement — so the report's doctor/clinic split reconciles to the cent
/// (exit criterion + task 17). The clinic share is then distributed among the active partners (M10
/// <see cref="IProfitDistributionService"/>) as of the batch's close date (or today if still open).
/// </summary>
public sealed class ProfitPerBatchReportService
{
    private readonly IEntitlementService _entitlements;
    private readonly IProfitDistributionService _distribution;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;

    public ProfitPerBatchReportService(
        IEntitlementService entitlements,
        IProfitDistributionService distribution,
        ICurrentUserAccessor currentUser,
        IClock clock)
    {
        _entitlements = entitlements;
        _distribution = distribution;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<ProfitPerBatchReportResponse> BuildAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var envId = _currentUser.EnvironmentId
            ?? throw new ForbiddenException("unauthenticated", "Authentication required.");

        var breakdown = await _entitlements.ExplainForBatchAsync(batchId, cancellationToken);

        var asOf = breakdown.EndDate ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var split = await _distribution.DistributeAsync(breakdown.ClinicShare, envId, asOf, cancellationToken);

        return new ProfitPerBatchReportResponse(
            breakdown.BatchId,
            breakdown.CustomerId,
            breakdown.DoctorId,
            breakdown.System,
            breakdown.Enabled,
            breakdown.Revenue,
            breakdown.DrugCost,
            breakdown.DrugProfit,
            breakdown.ExamFee,
            breakdown.DoctorShare,
            breakdown.CeilingApplied,
            breakdown.ClinicShare,
            asOf,
            split.DistributedTotal,
            split.Retained,
            split.Allocations,
            SettlementDiscount: breakdown.SettlementDiscount);
    }
}
