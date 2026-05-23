using VetSystem.Application.Partnership;

namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// Clinic-profits report (M12 task 3, PRD §7.9 + §6.8). Per the configured policy, <see cref="NetProfit"/>
/// is the clinic's <b>gross margin</b> — ex-tax revenue minus cost-of-goods-sold — and that net is what
/// the partnership split (M10) divides among the active partners as of <see cref="AsOf"/>.
/// <see cref="DoctorShares"/> is reported as a separate line for context and is <b>not</b> subtracted
/// from <see cref="NetProfit"/> (doctor pay is treated as a separate payout, not a cost of clinic profit).
/// <see cref="RetainedByClinic"/> is the portion of the net the partners' shares do not cover
/// (Σ shares &lt; 100%).
/// </summary>
public sealed record ClinicProfitsReportResponse(
    DateOnly? From,
    DateOnly? To,
    DateOnly AsOf,
    decimal Revenue,
    decimal Cogs,
    decimal NetProfit,
    decimal DoctorShares,
    decimal DistributedToPartners,
    decimal RetainedByClinic,
    IReadOnlyList<ProfitAllocation> PartnerAllocations);
