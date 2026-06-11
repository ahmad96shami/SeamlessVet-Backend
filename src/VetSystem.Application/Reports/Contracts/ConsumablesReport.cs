namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// One (location, product) line in the consumables report (M27, PRD §8 internal use).
/// <see cref="Quantity"/> is the total magnitude consumed at this location over the window (the
/// positive of the <c>consume</c> movements' negative deltas); <see cref="Cost"/> is the FEFO cost of
/// that consumption — <c>Σ qty × inventory_movements.unit_cost</c>, the per-leg weighted-average cost
/// snapshotted when each consumption FEFO-deducted its lots (M25 costing, so it reconciles to the
/// lots actually drawn rather than the catalog price).
/// </summary>
public sealed record ConsumablesReportRow(
    Guid ProductId,
    string ProductName,
    string LocationType,
    Guid LocationId,
    decimal Quantity,
    decimal Cost);

/// <summary>
/// Consumables report (M27, PRD §8): internal-use consumption of <c>is_consumable</c> products over a
/// period, summed by (location, product) and filterable by product and location. Each row aggregates
/// the window's <c>consume</c> <c>inventory_movements</c>; <see cref="TotalQuantity"/> /
/// <see cref="TotalCost"/> span the whole filtered set, <see cref="Rows"/> the requested page.
/// </summary>
public sealed record ConsumablesReportResponse(
    DateOnly? From,
    DateOnly? To,
    Guid? ProductId,
    string? LocationType,
    Guid? LocationId,
    decimal TotalQuantity,
    decimal TotalCost,
    int TotalCount,
    IReadOnlyList<ConsumablesReportRow> Rows);
