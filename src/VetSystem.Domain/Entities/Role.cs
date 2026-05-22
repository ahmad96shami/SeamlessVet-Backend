using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>SCHEMA §1 — RBAC role keyed by <see cref="RoleKey"/>. Per-environment unique on <c>key</c>.</summary>
public sealed class Role : Entity
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public static class RoleKey
{
    public const string Admin = "admin";
    public const string Accountant = "accountant";
    public const string VetClinic = "vet_clinic";
    public const string VetField = "vet_field";
    public const string VetBoth = "vet_both";
    public const string Receptionist = "receptionist";
    public const string Cashier = "cashier";
    public const string InventoryStaff = "inventory_staff";

    public static readonly IReadOnlyCollection<string> All =
    [
        Admin, Accountant, VetClinic, VetField, VetBoth,
        Receptionist, Cashier, InventoryStaff,
    ];
}
