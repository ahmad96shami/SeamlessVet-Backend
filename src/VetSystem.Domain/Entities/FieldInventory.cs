using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §4 — a field doctor's vehicle-style "moving warehouse" (PRD §6.4). One per field
/// doctor, auto-created when an admin approves a <c>vet_field</c>/<c>vet_both</c> registration
/// (M4 task 3). Its stock lives in <see cref="StockItem"/> rows with
/// <c>location_type = 'field'</c> and <c>location_id</c> equal to this row's id; load/unload
/// move stock between the central warehouse and here via signed <see cref="InventoryMovement"/>s.
/// </summary>
public sealed class FieldInventory : Entity
{
    public Guid DoctorId { get; set; }
}
