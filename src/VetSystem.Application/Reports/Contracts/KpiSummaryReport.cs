namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// Admin-dashboard KPI summary (M12 task 14, PRD §7.9). Snapshot figures as of <see cref="AsOf"/>
/// (UTC day): visits created today, ex-tax net-of-void revenue this calendar month, entitlements still
/// awaiting settlement, and the count of (location, product) balances at/below their low-stock threshold
/// (reusing the M4 inventory scan).
/// </summary>
public sealed record KpiSummaryResponse(
    DateOnly AsOf,
    int VisitsToday,
    decimal RevenueThisMonth,
    int PendingEntitlements,
    int LowStockItems);
