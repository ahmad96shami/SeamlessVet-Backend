namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// One (location, product) line in the inventory-movement report (M12 task 9, PRD §7.9).
/// <see cref="Inflows"/>/<see cref="Outflows"/> are the period's signed movement deltas attributed to
/// this location (a delta lands on its <c>to</c> location when positive, its <c>from</c> location when
/// negative — SCHEMA §4). <see cref="NetChange"/> = inflows − outflows for the period.
/// <see cref="Balance"/> is the <b>current</b> on-hand quantity (materialized <c>stock_items.quantity</c>);
/// over an all-time window it equals <see cref="NetChange"/> — the reconciliation guarantee (task 18).
/// </summary>
public sealed record InventoryMovementRow(
    string LocationType,
    Guid LocationId,
    Guid ProductId,
    decimal Inflows,
    decimal Outflows,
    decimal NetChange,
    decimal Balance);

/// <summary>
/// Inventory-movement report (PRD §7.9): inflows/outflows/balance per (location, product) over a period,
/// filterable by product and location. Rows are the (location, product) pairs with movement activity in
/// the window; <see cref="TotalCount"/> is the whole filtered set, <see cref="Rows"/> the requested page.
/// </summary>
public sealed record InventoryMovementReportResponse(
    DateOnly? From,
    DateOnly? To,
    Guid? ProductId,
    string? LocationType,
    Guid? LocationId,
    int TotalCount,
    IReadOnlyList<InventoryMovementRow> Rows);
