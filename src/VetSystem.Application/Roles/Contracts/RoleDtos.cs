namespace VetSystem.Application.Roles.Contracts;

/// <summary>
/// A role in the admin Roles tab (GET /admin/roles). <see cref="IsBuiltIn"/> marks the eight
/// seeded <c>RoleKey</c> values — those can have their permissions edited but cannot be renamed-by-key
/// or deleted; the <c>admin</c> role is fully protected. <see cref="Permissions"/> is the role's
/// current set of permission keys (from <c>role_permissions</c>).
/// </summary>
public sealed record RoleListItem(
    Guid Id,
    string Key,
    string Name,
    bool IsBuiltIn,
    int UserCount,
    IReadOnlyList<string> Permissions);

/// <summary>
/// POST /admin/roles — create a custom role. The key is generated server-side (e.g. "custom_xxxxxxxx");
/// the admin supplies a display name and the permission set (a subset of the catalog).
/// </summary>
public sealed record CreateRoleRequest(string Name, IReadOnlyList<string> Permissions);

/// <summary>
/// PATCH /admin/roles/{id} — rename + replace the role's full permission set. For built-in roles only
/// the permissions change (the key is immutable); the <c>admin</c> role is rejected.
/// </summary>
public sealed record UpdateRoleRequest(string Name, IReadOnlyList<string> Permissions);

/// <summary>An entry in the permission catalog (GET /admin/permissions) used to build the role editor.</summary>
public sealed record PermissionCatalogItem(string Key, string? Description);
