using VetSystem.Application.Partnership;

namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// Profit-per-farm-batch report (M12 task 4, PRD §7.9; M28 reformulation). The cost/revenue breakdown,
/// doctor share and clinic share are taken verbatim from the entitlement engine
/// (<c>ExplainForBatchAsync</c>), so <see cref="DoctorShare"/> reconciles to the persisted entitlement to
/// the cent (task 17). <see cref="ExamFee"/> is the supervision fee = the doctor's entitlement when
/// enabled; <see cref="FeeAddedToSettlement"/> is the System-B fee the farmer pays on top, and
/// <see cref="FeeRetainedByClinic"/> the fee the clinic keeps when the toggle is off (System B). The
/// clinic share (which may be negative) is split among the partners (M10) as of the batch's close date
/// (<see cref="AsOf"/>).
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
    decimal ClinicShare,
    DateOnly AsOf,
    decimal DistributedToPartners,
    decimal RetainedByClinic,
    IReadOnlyList<ProfitAllocation> PartnerAllocations,
    decimal FeeAddedToSettlement = 0m,  // M28 — System-B fee charged to the farmer on top
    decimal FeeRetainedByClinic = 0m,   // M28 — fee kept by the clinic when the toggle is off (System B)
    decimal SettlementDiscount = 0m);   // M24 — تصفية discount; already netted out of ClinicShare
