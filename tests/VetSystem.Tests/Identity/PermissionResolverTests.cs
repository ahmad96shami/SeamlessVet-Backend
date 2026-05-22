using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class PermissionResolverTests
{
    [Fact]
    public async Task ResolveAsync_GrantOverride_AddsPermissionAbsentFromRole()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var (db, user, perms) = await SeedAsync(scope);

        await GrantOverrideAsync(db, scope.EnvironmentId, user.Id, perms[PermissionKey.ReportsRead], OverrideEffect.Grant);

        var resolver = new PermissionResolver(db, new MemoryCache(new MemoryCacheOptions()));
        var effective = await resolver.ResolveAsync(user.Id, scope.EnvironmentId, CancellationToken.None);

        effective.Should().Contain(PermissionKey.UsersApprove, "role default still applies");
        effective.Should().Contain(PermissionKey.ReportsRead, "grant override adds a permission the role didn't have");
    }

    [Fact]
    public async Task ResolveAsync_DenyOverride_RemovesPermissionFromRole()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var (db, user, perms) = await SeedAsync(scope);

        await GrantOverrideAsync(db, scope.EnvironmentId, user.Id, perms[PermissionKey.UsersApprove], OverrideEffect.Deny);

        var resolver = new PermissionResolver(db, new MemoryCache(new MemoryCacheOptions()));
        var effective = await resolver.ResolveAsync(user.Id, scope.EnvironmentId, CancellationToken.None);

        effective.Should().NotContain(PermissionKey.UsersApprove, "deny override strips a role default");
    }

    [Fact]
    public async Task ResolveAsync_CachesPerUserUntilInvalidate()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var (db, user, perms) = await SeedAsync(scope);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new PermissionResolver(db, cache);

        var first = await resolver.ResolveAsync(user.Id, scope.EnvironmentId, CancellationToken.None);
        first.Should().Contain(PermissionKey.UsersApprove);

        await GrantOverrideAsync(db, scope.EnvironmentId, user.Id, perms[PermissionKey.UsersApprove], OverrideEffect.Deny);

        var cachedHit = await resolver.ResolveAsync(user.Id, scope.EnvironmentId, CancellationToken.None);
        cachedHit.Should().Contain(PermissionKey.UsersApprove, "cached snapshot survives until explicit invalidation");

        resolver.Invalidate(user.Id);

        var afterInvalidation = await resolver.ResolveAsync(user.Id, scope.EnvironmentId, CancellationToken.None);
        afterInvalidation.Should().NotContain(PermissionKey.UsersApprove);
    }

    private static async Task<(ApplicationDbContext db, User user, IReadOnlyDictionary<string, Guid> perms)> SeedAsync(PgTestScope scope)
    {
        var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var role = new Role
        {
            EnvironmentId = scope.EnvironmentId,
            Key = RoleKey.Admin,
            Name = "Admin",
        };
        db.Roles.Add(role);

        var permissionRows = PermissionKey.All
            .Select(k => new Permission { EnvironmentId = scope.EnvironmentId, Key = k, Description = k })
            .ToList();
        db.Permissions.AddRange(permissionRows);
        await db.SaveChangesAsync();

        var perms = permissionRows.ToDictionary(p => p.Key, p => p.Id);

        db.RolePermissions.Add(new RolePermission
        {
            RoleId = role.Id,
            PermissionId = perms[PermissionKey.UsersApprove],
        });

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Test User",
            PhonePrimary = $"+98{Guid.NewGuid().ToString("N").Substring(0, 9)}",
            PasswordHash = "irrelevant",
            Status = UserStatus.Active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (db, user, perms);
    }

    private static async Task GrantOverrideAsync(
        ApplicationDbContext db,
        Guid environmentId,
        Guid userId,
        Guid permissionId,
        string effect)
    {
        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            EnvironmentId = environmentId,
            UserId = userId,
            PermissionId = permissionId,
            Effect = effect,
        });
        await db.SaveChangesAsync();
    }
}
