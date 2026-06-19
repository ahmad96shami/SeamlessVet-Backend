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
    IReadOnlyList<ProfitAllocation> PartnerAllocations,
    decimal SettlementDiscounts = 0m, // M24 — تصفية discounts granted in the window (already netted out of Revenue)
    // Operating expenses (water/electricity/rent/…) recognized in the window. NetOperatingProfit is the
    // headline "صافي ربح المركز" the UI shows (gross margin minus operating expenses). The Payables*
    // figures are a current snapshot of what the center owes others (as-of-now, independent of the
    // window); NetAfterObligations = NetOperatingProfit − PayablesOutstanding is the bottom-line card.
    decimal OperatingExpenses = 0m,
    decimal NetOperatingProfit = 0m,
    decimal PayablesSuppliers = 0m,
    decimal PayablesDoctorPartners = 0m,
    decimal PayablesEmployees = 0m,
    decimal PayablesUnpaidExpenses = 0m,
    decimal PayablesOutstanding = 0m,
    decimal NetAfterObligations = 0m);
