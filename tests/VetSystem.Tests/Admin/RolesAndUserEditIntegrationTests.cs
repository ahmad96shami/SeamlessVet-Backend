using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Admin;

/// <summary>
/// Roles tab (custom roles + editing built-in role permissions) and the edit-user flow. Admin role is
/// protected; built-in roles can have their permissions edited but not deleted; custom roles are pure
/// permission bundles assignable to users.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RolesAndUserEditIntegrationTests
{
    [Fact]
    public async Task CreateCustomRole_AssignsToUser_AndResolvesItsPermissions()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var created = await CreateRoleAsync(client, "مشرف المخزن",
            [PermissionKey.InventoryAdjust, PermissionKey.CatalogWrite]);
        var roleKey = created.GetProperty("key").GetString()!;
        var roleId = created.GetProperty("id").GetGuid();
        created.GetProperty("isBuiltIn").GetBoolean().Should().BeFalse();
        roleKey.Should().StartWith("custom_");

        // It shows up in the roster.
        var list = await client.GetFromJsonAsync<JsonElement>("/admin/roles");
        list.EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == roleId);

        // It is assignable to a new user.
        var userId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/admin/users", new
        {
            id = userId,
            fullName = "موظف مخزن",
            phonePrimary = Phone(),
            email = (string?)null,
            password = "Passw0rd!",
            roleKey,
            licenseNumber = (string?)null,
            licenseDetails = (string?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var savedUser = await db.Users.AsNoTracking().FirstAsync(u => u.RoleId == roleId);
        var rolePerms = await db.RolePermissions.CountAsync(rp => rp.RoleId == roleId);
        rolePerms.Should().Be(2);

        // The custom role's permissions resolve for that user.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new PermissionResolver(db, cache);
        var perms = await resolver.ResolveAsync(savedUser.Id, scope.EnvironmentId, default);
        perms.Should().BeEquivalentTo([PermissionKey.InventoryAdjust, PermissionKey.CatalogWrite]);
    }

    [Fact]
    public async Task EditBuiltInRole_ReplacesItsPermissionSet()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var cashierId = await RoleIdAsync(client, RoleKey.Cashier);

        var resp = await PatchAsync(client, $"/admin/roles/{cashierId}", new
        {
            name = "أمين الصندوق",
            permissions = new[] { PermissionKey.InvoicesWrite, PermissionKey.ReportsRead },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var keys = await db.RolePermissions.Where(rp => rp.RoleId == cashierId)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Key)
            .ToListAsync();
        keys.Should().BeEquivalentTo([PermissionKey.InvoicesWrite, PermissionKey.ReportsRead]);
    }

    [Fact]
    public async Task EditAdminRole_IsRejected()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var adminRoleId = await RoleIdAsync(client, RoleKey.Admin);
        var resp = await PatchAsync(client, $"/admin/roles/{adminRoleId}", new
        {
            name = "Administrator",
            permissions = new[] { PermissionKey.InvoicesWrite },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, "the admin role is protected from edits");
    }

    [Fact]
    public async Task DeleteRole_BuiltInOrInUse_IsBlocked_OtherwiseSucceeds()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // Built-in role cannot be deleted.
        var cashierId = await RoleIdAsync(client, RoleKey.Cashier);
        (await DeleteAsync(client, $"/admin/roles/{cashierId}")).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        // Custom role assigned to a user cannot be deleted.
        var inUse = await CreateRoleAsync(client, "دور قيد الاستخدام", [PermissionKey.CustomersWrite]);
        var inUseKey = inUse.GetProperty("key").GetString()!;
        var inUseId = inUse.GetProperty("id").GetGuid();
        (await PostAsync(client, "/admin/users", new
        {
            id = Guid.CreateVersion7(),
            fullName = "حامل الدور",
            phonePrimary = Phone(),
            email = (string?)null,
            password = "Passw0rd!",
            roleKey = inUseKey,
            licenseNumber = (string?)null,
            licenseDetails = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DeleteAsync(client, $"/admin/roles/{inUseId}")).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "a role assigned to users cannot be deleted");

        // Unused custom role deletes (soft).
        var unused = await CreateRoleAsync(client, "دور غير مستخدم", [PermissionKey.CustomersWrite]);
        var unusedId = unused.GetProperty("id").GetGuid();
        (await DeleteAsync(client, $"/admin/roles/{unusedId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EditUser_UpdatesProfileAndRole()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var createResp = await PostAsync(client, "/admin/users", new
        {
            fullName = "الاسم القديم",
            phonePrimary = Phone(),
            email = "old@example.com",
            password = "Passw0rd!",
            roleKey = RoleKey.Cashier,
            licenseNumber = (string?)null,
            licenseDetails = (string?)null,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        // Users are server-id'd (no client GUID) — use the returned id.
        var userId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var newPhone = Phone();
        var resp = await PatchAsync(client, $"/admin/users/{userId}", new
        {
            fullName = "الاسم الجديد",
            phonePrimary = newPhone,
            email = "new@example.com",
            roleKey = RoleKey.Accountant,
            licenseNumber = (string?)null,
            licenseDetails = (string?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var accountantRoleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.Accountant)
            .Select(r => r.Id).FirstAsync();
        var saved = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        saved.FullName.Should().Be("الاسم الجديد");
        saved.PhonePrimary.Should().Be(newPhone);
        saved.Email.Should().Be("new@example.com");
        saved.RoleId.Should().Be(accountantRoleId);
    }

    [Fact]
    public async Task PermissionCatalog_ListsNewKeys()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var catalog = await client.GetFromJsonAsync<JsonElement>("/admin/permissions");
        var keys = catalog.EnumerateArray().Select(p => p.GetProperty("key").GetString()).ToList();
        keys.Should().Contain(PermissionKey.RolesManage);
        keys.Should().Contain(PermissionKey.OperatingExpensesManage);
    }

    [Fact]
    public async Task RolesEndpoints_RequireRolesManagePermission()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var cashier = await SeedUserWithRoleAsync(scope, RoleKey.Cashier); // no roles.manage
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, cashier, role: RoleKey.Cashier);

        (await client.GetAsync("/admin/roles")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, User user, string role = "admin")
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, role));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<JsonElement> CreateRoleAsync(HttpClient client, string name, string[] permissions)
    {
        var resp = await PostAsync(client, "/admin/roles", new { name, permissions });
        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "create-role body: {0}", body);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> RoleIdAsync(HttpClient client, string roleKey)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/admin/roles");
        return list.EnumerateArray().First(r => r.GetProperty("key").GetString() == roleKey)
            .GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
        => SendAsync(client, HttpMethod.Post, path, body);

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, object body)
        => SendAsync(client, HttpMethod.Patch, path, body);

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path)
        => SendAsync(client, HttpMethod.Delete, path, null);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    /// <summary>A regex-valid numeric phone (the user validator rejects hex letters).</summary>
    private static string Phone() => "+9705" + Random.Shared.Next(1000000, 9999999).ToString();

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<User> SeedUserWithRoleAsync(PgTestScope scope, string roleKey)
    {
        await using var db = NewContext(scope, null);
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Cashier",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"C{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
