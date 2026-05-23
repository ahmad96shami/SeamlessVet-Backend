using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §4 — the materialized current quantity of a product at a single location
/// (<c>warehouse</c> or <c>field</c>, via the polymorphic <see cref="LocationType"/> +
/// <see cref="LocationId"/>). Balances are <b>derived</b> from <see cref="InventoryMovement"/>
/// rows and recomputed server-side; clients never write an absolute quantity here
/// (SCHEMA "Key invariants" #2). <see cref="Quantity"/> always equals the signed sum of the
/// movement deltas whose affected location is this (<see cref="LocationType"/>,
/// <see cref="LocationId"/>, <see cref="ProductId"/>).
/// </summary>
public sealed class StockItem : Entity
{
    public string LocationType { get; set; } = StockLocation.Warehouse;

    public Guid LocationId { get; set; }

    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }
}

public static class StockLocation
{
    public const string Warehouse = "warehouse";
    public const string Field = "field";

    public static readonly IReadOnlyCollection<string> All = [Warehouse, Field];
}
