using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Tests.Infrastructure;

/// <summary>
/// Seeds the per-env identity rows admin endpoints need (roles, permissions, role_permissions,
/// system_settings, an active admin user). Returns the admin user so tests can mint a JWT for it.
/// </summary>
internal static class AdminTestSeed
{
    public static async Task<User> SeedAdminAsync(PgTestScope scope, IReadOnlyCollection<string>? extraPermissions = null)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        await SeedRolesAsync(db, scope.EnvironmentId);
        await SeedPermissionsAsync(db, scope.EnvironmentId);
        await SeedAdminRolePermissionsAsync(db, scope.EnvironmentId);
        await SeedSystemSettingsAsync(db, scope.EnvironmentId);
        var admin = await SeedAdminUserAsync(db, scope.EnvironmentId);

        if (extraPermissions is { Count: > 0 })
        {
            foreach (var permKey in extraPermissions)
            {
                var perm = await db.Permissions.IgnoreQueryFilters()
                    .FirstAsync(p => p.EnvironmentId == scope.EnvironmentId && p.Key == permKey);

                var alreadyOnRole = await db.RolePermissions
                    .AnyAsync(rp => rp.RoleId == admin.RoleId && rp.PermissionId == perm.Id);

                if (!alreadyOnRole)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = admin.RoleId,
                        PermissionId = perm.Id,
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        return admin;
    }

    private static async Task SeedRolesAsync(ApplicationDbContext db, Guid envId)
    {
        foreach (var key in RoleKey.All)
        {
            db.Roles.Add(new Role
            {
                EnvironmentId = envId,
                Key = key,
                Name = key,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext db, Guid envId)
    {
        foreach (var key in PermissionKey.All)
        {
            db.Permissions.Add(new Permission
            {
                EnvironmentId = envId,
                Key = key,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminRolePermissionsAsync(ApplicationDbContext db, Guid envId)
    {
        var adminRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == envId && r.Key == RoleKey.Admin);

        var perms = await db.Permissions.IgnoreQueryFilters()
            .Where(p => p.EnvironmentId == envId)
            .ToListAsync();

        foreach (var perm in perms)
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = adminRole.Id,
                PermissionId = perm.Id,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedSystemSettingsAsync(ApplicationDbContext db, Guid envId)
    {
        db.SystemSettings.Add(new SystemSettings
        {
            EnvironmentId = envId,
            DefaultExamFee = 0m,
            EntitlementEnabledGlobal = true,
            LowStockThresholdPct = 0m,
            ExpirationWarningDays = 30,
            TaxEnabled = false,
            TaxRate = 0m,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedAdminUserAsync(ApplicationDbContext db, Guid envId)
    {
        var adminRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == envId && r.Key == RoleKey.Admin);

        var hasher = new BCryptPasswordHasher();
        var admin = new User
        {
            EnvironmentId = envId,
            RoleId = adminRole.Id,
            FullName = "Integration Admin",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = hasher.Hash("AdminTest_pw_1!"),
            Status = UserStatus.Active,
            NumberPrefix = $"A{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }
}
