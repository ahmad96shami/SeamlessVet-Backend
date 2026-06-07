using VetSystem.Application.Partnership;

namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// Profit-per-farm-batch report (M12 task 4, PRD §7.9). The cost/revenue breakdown, doctor share and
/// clinic share are taken verbatim from the M9 entitlement engine (<c>ExplainForBatchAsync</c>), so
/// <see cref="DoctorShare"/> reconciles to the persisted entitlement to the cent (task 17). The clinic
/// share is then split among the partners (M10) as of the batch's close date (<see cref="AsOf"/>).
/// </summary>
public sealed record ProfitPerBatchReportResponse(
    Guid BatchId,
    Guid CustomerId,
    Guid DoctorId,
    string EntitlementSystem,
    bool EntitlementEnabled,
    decimal Revenue,
    decimal DrugCost,
    decimal DrugProfit,
    decimal ExamFee,
    decimal DoctorShare,
    decimal? CeilingApplied,
    decimal ClinicShare,
    DateOnly AsOf,
    decimal DistributedToPartners,
    decimal RetainedByClinic,
    IReadOnlyList<ProfitAllocation> PartnerAllocations,
    decimal SettlementDiscount = 0m); // M24 — تصفية discount; already netted out of ClinicShare
