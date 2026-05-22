namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §1 — pure junction table (composite PK <c>(role_id, permission_id)</c>); not a syncable
/// entity, no <c>environment_id</c> or soft-delete columns.
/// </summary>
public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}
