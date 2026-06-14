using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Identity;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Platform;

/// <summary>
/// M35 — the platform super-admin console. Login mints a <c>platform_admin</c> token (no env);
/// provisioning a center makes its admin loginable; suspend/reactivate flips the live gate; and
/// isolation holds both ways (a platform token reads no tenant data; a tenant token is barred from
/// <c>/platform/*</c>).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PlatformConsoleTests
{
    private const string PlatformPassword = "Platform_pw_1!";

    [Fact]
    public async Task Login_issues_a_platform_admin_token_with_no_env_or_role_claim()
    {
        await using var factory = new VetApiFactory();
        var phone = UniquePhone();
        var adminId = await SeedPlatformAdminAsync(phone, PlatformPassword);
        try
        {
            using var client = factory.CreateClient();
            var body = await PlatformLoginAsync(client, phone, PlatformPassword);

            var token = body.GetProperty("accessToken").GetString()!;
            body.GetProperty("platformAdminId").GetString().Should().Be(adminId.ToString());

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            jwt.Claims.Should().Contain(c => c.Type == HttpCurrentUserAccessor.PlatformAdminClaim && c.Value == "true");
            jwt.Claims.Should().NotContain(c => c.Type == HttpCurrentUserAccessor.EnvironmentIdClaim);
            jwt.Claims.Should().NotContain(c => c.Type == "role" || c.Type == "perms");
            jwt.Subject.Should().Be(adminId.ToString());
        }
        finally
        {
            await DeletePlatformAdminAsync(adminId);
        }
    }

    [Fact]
    public async Task Login_rejects_bad_password_and_suspended_account()
    {
        await using var factory = new VetApiFactory();
        var activePhone = UniquePhone();
        var suspendedPhone = UniquePhone();
        var activeId = await SeedPlatformAdminAsync(activePhone, PlatformPassword);
        var suspendedId = await SeedPlatformAdminAsync(suspendedPhone, PlatformPassword, PlatformAdminStatus.Suspended);
        try
        {
            using var client = factory.CreateClient();

            var badPw = await client.PostAsJsonAsync("/platform/auth/login",
                new { phone = activePhone, password = "wrong-password" });
            badPw.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await badPw.Content.ReadAsStringAsync()).Should().Contain("invalid_credentials");

            var suspended = await client.PostAsJsonAsync("/platform/auth/login",
                new { phone = suspendedPhone, password = PlatformPassword });
            suspended.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await suspended.Content.ReadAsStringAsync()).Should().Contain("platform_account_suspended");
        }
        finally
        {
            await DeletePlatformAdminAsync(activeId);
            await DeletePlatformAdminAsync(suspendedId);
        }
    }

    [Fact]
    public async Task Provisioned_center_admin_can_log_in()
    {
        await using var factory = new VetApiFactory();
        var platformPhone = UniquePhone();
        var platformId = await SeedPlatformAdminAsync(platformPhone, PlatformPassword);
        Guid tenantId = Guid.Empty;
        try
        {
            using var client = factory.CreateClient();
            await AuthenticateAsPlatformAsync(client, platformPhone);

            var suffix = Suffix();
            var centerAdminPhone = DigitPhone();
            var centerAdminPassword = "Center_pw_1!";
            var created = await client.PostAsJsonAsync("/platform/tenants", new
            {
                centerName = $"Center {suffix}",
                code = $"PLAT-{suffix}",
                mode = EnvironmentMode.Solo,
                adminFullName = "Center Owner",
                adminPhone = centerAdminPhone,
                adminPassword = centerAdminPassword,
                adminEmail = (string?)null,
            });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var tenant = await created.Content.ReadFromJsonAsync<JsonElement>();
            tenantId = Guid.Parse(tenant.GetProperty("id").GetString()!);
            tenant.GetProperty("status").GetString().Should().Be(EnvironmentStatus.Active);
            tenant.GetProperty("userCount").GetInt32().Should().Be(1, "the first admin is the only user");

            // The provisioned admin authenticates against the new center — no Authorization header.
            using var anonClient = factory.CreateClient();
            var login = await anonClient.PostAsJsonAsync("/auth/login",
                new { environmentId = tenantId, phonePrimary = centerAdminPhone, password = centerAdminPassword });
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            (await login.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("roleKey").GetString().Should().Be(RoleKey.Admin);
        }
        finally
        {
            if (tenantId != Guid.Empty) await DeleteEnvAsync(tenantId);
            await DeletePlatformAdminAsync(platformId);
        }
    }

    [Fact]
    public async Task Suspend_makes_the_center_user_get_403_and_reactivate_restores()
    {
        await using var factory = new VetApiFactory();
        var platformPhone = UniquePhone();
        var platformId = await SeedPlatformAdminAsync(platformPhone, PlatformPassword);
        Guid tenantId = Guid.Empty;
        try
        {
            using var platformClient = factory.CreateClient();
            await AuthenticateAsPlatformAsync(platformClient, platformPhone);

            var suffix = Suffix();
            var centerAdminPhone = DigitPhone();
            const string centerAdminPassword = "Center_pw_1!";
            var created = await platformClient.PostAsJsonAsync("/platform/tenants", new
            {
                centerName = $"Center {suffix}",
                code = $"PLAT-{suffix}",
                mode = EnvironmentMode.Solo,
                adminFullName = "Center Owner",
                adminPhone = centerAdminPhone,
                adminPassword = centerAdminPassword,
                adminEmail = (string?)null,
            });
            tenantId = Guid.Parse((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);

            // The center admin logs in and holds a tenant token.
            using var tenantClient = factory.CreateClient();
            var login = await tenantClient.PostAsJsonAsync("/auth/login",
                new { environmentId = tenantId, phonePrimary = centerAdminPhone, password = centerAdminPassword });
            var tenantToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);

            // Active → an already-issued tenant token passes the gate.
            (await tenantClient.PostAsync("/auth/powersync-token", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Suspend (Invalidate flips the cache within one request).
            (await platformClient.PostAsync($"/platform/tenants/{tenantId}/suspend", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var blocked = await tenantClient.PostAsync("/auth/powersync-token", null);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await blocked.Content.ReadAsStringAsync()).Should().Contain("environment_suspended");

            // Reactivate restores.
            (await platformClient.PostAsync($"/platform/tenants/{tenantId}/reactivate", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await tenantClient.PostAsync("/auth/powersync-token", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            if (tenantId != Guid.Empty) await DeleteEnvAsync(tenantId);
            await DeletePlatformAdminAsync(platformId);
        }
    }

    [Fact]
    public async Task Platform_token_cannot_read_tenant_data()
    {
        await using var factory = new VetApiFactory();
        var platformPhone = UniquePhone();
        var platformId = await SeedPlatformAdminAsync(platformPhone, PlatformPassword);
        try
        {
            using var client = factory.CreateClient();
            await AuthenticateAsPlatformAsync(client, platformPhone);

            // A tenant, permission-gated route: the platform token has no environment_id, so the
            // RequirePermission null-env guard rejects it (and the env-scoped filter would hide rows).
            var response = await client.GetAsync("/admin/users");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await DeletePlatformAdminAsync(platformId);
        }
    }

    [Fact]
    public async Task Tenant_token_is_rejected_on_platform_routes()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        using var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, RoleKey.Admin));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var response = await client.GetAsync("/platform/tenants");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("platform_admin_required");
    }

    // ---- helpers ----

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static string UniquePhone() => $"+9709{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Digits-only phone — the provisioning validator enforces a numeric phone format.</summary>
    private static string DigitPhone() => $"+9705{Math.Abs(Guid.NewGuid().GetHashCode())}";

    private static async Task<JsonElement> PlatformLoginAsync(HttpClient client, string phone, string password)
    {
        var response = await client.PostAsJsonAsync("/platform/auth/login", new { phone, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AuthenticateAsPlatformAsync(HttpClient client, string phone)
    {
        var body = await PlatformLoginAsync(client, phone, PlatformPassword);
        var token = body.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<Guid> SeedPlatformAdminAsync(
        string phone, string password, string status = PlatformAdminStatus.Active)
    {
        await using var db = Db();
        var admin = new PlatformAdmin
        {
            Id = Guid.CreateVersion7(),
            FullName = "Platform Tester",
            Phone = phone,
            PasswordHash = new BCryptPasswordHasher().Hash(password),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task DeletePlatformAdminAsync(Guid id)
    {
        await using var db = Db();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM platform_admins WHERE id = {id};");
    }

    private static async Task DeleteEnvAsync(Guid environmentId)
    {
        await using var db = Db();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM environments WHERE id = {environmentId};");
    }

    private static ApplicationDbContext Db()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(PgTestScope.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new FakeCurrentUser());
}
