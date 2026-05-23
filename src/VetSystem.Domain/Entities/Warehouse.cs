using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §4 — central warehouse, typically one per environment (auto-created on bootstrap,
/// M4 task 2). Stock balances live in <see cref="StockItem"/> rows whose
/// <c>location_type = 'warehouse'</c> and <c>location_id</c> equals this warehouse's id.
/// </summary>
public sealed class Warehouse : Entity
{
    public string Name { get; set; } = "Central";
}
