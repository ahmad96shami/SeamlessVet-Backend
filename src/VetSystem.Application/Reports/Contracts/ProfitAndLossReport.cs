namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// Simplified P&amp;L (M12 task 8, PRD §7.9) — the clinic income statement for a period. Per the
/// gross-margin policy, <see cref="GrossProfit"/> = ex-tax <see cref="Revenue"/> − <see cref="Cogs"/>;
/// <see cref="TaxCollected"/> (a remitted liability, not income) and <see cref="DoctorShares"/> (a
/// separate payout) are shown as memo lines, not netted into gross profit. No general expense ledger
/// exists, so this is the fullest P&amp;L the schema supports.
/// </summary>
public sealed record ProfitAndLossResponse(
    DateOnly? From,
    DateOnly? To,
    decimal Revenue,
    decimal TaxCollected,
    decimal Cogs,
    decimal GrossProfit,
    decimal DoctorShares,
    decimal SettlementDiscounts = 0m); // M24 — تصفية discounts granted in the window (already netted out of Revenue)
