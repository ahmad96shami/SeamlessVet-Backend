using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Roles.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Roles;

/// <summary>
/// Admin management of roles and their permissions (the Roles tab). Roles are DB-backed and
/// env-scoped by the global query filter. Built-in roles (keys in <see cref="RoleKey.All"/>) can have
/// their permissions edited but cannot be renamed-by-key or deleted; the <c>admin</c> role is fully
/// protected so an admin can never lock themselves out. Custom roles are pure permission bundles with
/// a generated <c>custom_*</c> key — they do not participate in the built-in-role-specific behaviors
/// (field-doctor sync, doctor pickers, notification routing).
///
/// On any permission change we invalidate the permission cache (<see cref="IPermissionResolver"/>) for
/// every user holding that role so the gate sees the change on their next request.
/// </summary>
public sealed class RoleAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IPermissionResolver _permissionResolver;

    public RoleAdminService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IPermissionResolver permissionResolver)
    {
        _db = db;
        _currentUser = currentUser;
        _permissionResolver = permissionResolver;
    }

    public async Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await _db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Key, r.Name })
            .ToListAsync(cancellationToken);

        if (roles.Count == 0)
        {
            return [];
        }

        var ids = roles.Select(r => r.Id).ToList();

        var permsByRole = (await _db.RolePermissions
            .Where(rp => ids.Contains(rp.RoleId))
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { rp.RoleId, p.Key })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).OrderBy(k => k).ToList());

        var userCounts = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.RoleId))
            .GroupBy(u => u.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        return roles.Select(r => new RoleListItem(
            r.Id,
            r.Key,
            r.Name,
            RoleKey.All.Contains(r.Key),
            userCounts.GetValueOrDefault(r.Id),
            permsByRole.GetValueOrDefault(r.Id) ?? [])).ToList();
    }

    public async Task<IReadOnlyList<PermissionCatalogItem>> GetPermissionCatalogAsync(CancellationToken cancellationToken)
        => await _db.Permissions.AsNoTracking()
            .OrderBy(p => p.Key)
            .Select(p => new PermissionCatalogItem(p.Key, p.Description))
            .ToListAsync(cancellationToken);

    public async Task<RoleListItem> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        var permissionIds = await ResolvePermissionIdsAsync(request.Permissions, cancellationToken);

        string key;
        do
        {
            key = "custom_" + Guid.NewGuid().ToString("N")[..8];
        }
        while (await _db.Roles.IgnoreQueryFilters().AnyAsync(r => r.Key == key, cancellationToken));

        var role = new Role { Key = key, Name = request.Name.Trim() };
        _db.Roles.Add(role);

        // Save first so the AuditingInterceptor stamps role.Id before the junction rows reference it.
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var permId in permissionIds)
        {
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permId });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new RoleListItem(role.Id, role.Key, role.Name, IsBuiltIn: false, UserCount: 0,
            request.Permissions.Distinct().OrderBy(k => k).ToList());
    }

    public async Task<RoleListItem> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                   ?? throw new NotFoundException("role", id);

        if (string.Equals(role.Key, RoleKey.Admin, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("role_protected", "The admin role cannot be modified.");
        }

        var permissionIds = await ResolvePermissionIdsAsync(request.Permissions, cancellationToken);

        role.Name = request.Name.Trim();

        // Replace the role's permission set: the junction has no soft-delete, so hard-remove + re-add.
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(cancellationToken);
        _db.RolePermissions.RemoveRange(existing);
        foreach (var permId in permissionIds)
        {
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permId });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateRoleHoldersAsync(role.Id, cancellationToken);

        var userCount = await _db.Users.AsNoTracking().CountAsync(u => u.RoleId == role.Id, cancellationToken);
        return new RoleListItem(role.Id, role.Key, role.Name, RoleKey.All.Contains(role.Key), userCount,
            request.Permissions.Distinct().OrderBy(k => k).ToList());
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                   ?? throw new NotFoundException("role", id);

        if (RoleKey.All.Contains(role.Key))
        {
            throw new ConflictException("role_builtin", "Built-in roles cannot be deleted.");
        }

        var inUse = await _db.Users.AnyAsync(u => u.RoleId == role.Id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException("role_in_use", "Cannot delete a role that is still assigned to users.");
        }

        var perms = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(cancellationToken);
        _db.RolePermissions.RemoveRange(perms);
        _db.Roles.Remove(role); // AuditingInterceptor converts Deleted → soft-delete.
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Maps requested permission keys to ids, rejecting any not in the env's catalog.</summary>
    private async Task<IReadOnlyList<Guid>> ResolvePermissionIdsAsync(
        IReadOnlyList<string> requested, CancellationToken cancellationToken)
    {
        var keys = requested.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var catalog = await _db.Permissions
            .Where(p => keys.Contains(p.Key))
            .ToDictionaryAsync(p => p.Key, p => p.Id, cancellationToken);

        var unknown = keys.Where(k => !catalog.ContainsKey(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ConflictException("unknown_permission", $"Unknown permission key(s): {string.Join(", ", unknown)}.");
        }

        return catalog.Values.ToList();
    }

    private async Task InvalidateRoleHoldersAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var userIds = await _db.Users.AsNoTracking()
            .Where(u => u.RoleId == roleId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            _permissionResolver.Invalidate(userId);
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
