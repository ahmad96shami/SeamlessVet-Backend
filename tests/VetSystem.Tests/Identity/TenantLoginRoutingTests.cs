using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

/// <summary>
/// M34 — tenant-routed login. <c>/auth/centers</c> lists the active centers a phone belongs to;
/// login is scoped to the chosen env; refresh derives the env off the stored token (so a shared
/// phone in two centers refreshes to the right one); suspended centers are excluded + rejected;
/// the anonymous lookups are IP-rate-limited.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TenantLoginRoutingTests
{
    private const string Password = "Shared_pw_1!";

    [Fact]
    public async Task Centers_lists_active_centers_for_a_phone_across_envs()
    {
        await using var envA = await PgTestScope.CreateAsync();
        await using var envB = await PgTestScope.CreateAsync();
        var phone = UniquePhone();
        await SeedUserAsync(envA, phone);
        await SeedUserAsync(envB, phone);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/centers", new { phone });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var centers = await ReadCentersAsync(response);
        centers.Select(c => c.GetProperty("environmentId").GetString())
            .Should().Contain([envA.EnvironmentId.ToString(), envB.EnvironmentId.ToString()]);
    }

    [Fact]
    public async Task Centers_excludes_suspended_centers_and_returns_empty_for_unknown_phone()
    {
        await using var envA = await PgTestScope.CreateAsync();
        await using var envB = await PgTestScope.CreateAsync();
        var phone = UniquePhone();
        await SeedUserAsync(envA, phone);
        await SeedUserAsync(envB, phone);
        await SetEnvStatusAsync(envB, EnvironmentStatus.Suspended);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var centers = await ReadCentersAsync(await client.PostAsJsonAsync("/auth/centers", new { phone }));
        var ids = centers.Select(c => c.GetProperty("environmentId").GetString()).ToList();
        ids.Should().Contain(envA.EnvironmentId.ToString());
        ids.Should().NotContain(envB.EnvironmentId.ToString(), "suspended centers are hidden from the picker");

        var none = await ReadCentersAsync(await client.PostAsJsonAsync("/auth/centers", new { phone = UniquePhone() }));
        none.Should().BeEmpty();
    }

    [Fact]
    public async Task CenterByCode_resolves_active_and_404s_unknown()
    {
        await using var env = await PgTestScope.CreateAsync();
        var code = await ReadCodeAsync(env);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var ok = await client.PostAsJsonAsync("/auth/center-by-code", new { code });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("environmentId").GetString().Should().Be(env.EnvironmentId.ToString());

        var missing = await client.PostAsJsonAsync("/auth/center-by-code", new { code = "NO-SUCH-CODE" });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_into_a_suspended_center_is_rejected()
    {
        await using var env = await PgTestScope.CreateAsync();
        var phone = UniquePhone();
        await SeedUserAsync(env, phone);
        await SetEnvStatusAsync(env, EnvironmentStatus.Suspended);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { environmentId = env.EnvironmentId, phonePrimary = phone, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("environment_suspended");
    }

    [Fact]
    public async Task Refresh_is_scoped_to_the_tokens_env_for_a_shared_phone()
    {
        await using var envA = await PgTestScope.CreateAsync();
        await using var envB = await PgTestScope.CreateAsync();
        var phone = UniquePhone();
        var userA = await SeedUserAsync(envA, phone);
        var userB = await SeedUserAsync(envB, phone);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var pairA = await LoginAsync(client, envA.EnvironmentId, phone);
        var pairB = await LoginAsync(client, envB.EnvironmentId, phone);
        pairA.GetProperty("userId").GetString().Should().Be(userA.ToString());
        pairB.GetProperty("userId").GetString().Should().Be(userB.ToString());

        // The bug this guards: the old env-predicate refresh would look the token up under the
        // wrong env. With env-from-token, each refresh resolves to its own center's user.
        var rotatedA = await RefreshAsync(client, pairA.GetProperty("refreshToken").GetString()!);
        var rotatedB = await RefreshAsync(client, pairB.GetProperty("refreshToken").GetString()!);
        rotatedA.GetProperty("userId").GetString().Should().Be(userA.ToString());
        rotatedB.GetProperty("userId").GetString().Should().Be(userB.ToString());
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited_per_ip()
    {
        await using var factory = new VetApiFactory { EnableRateLimiting = true, AuthTokenLimit = 2 };
        using var client = factory.CreateClient();
        var phone = UniquePhone();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/auth/centers", new { phone })).StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "the IP-partitioned auth bucket must trip on a burst");
    }

    // ---- helpers ----

    private static string UniquePhone() => $"+9705{Guid.NewGuid().ToString("N")[..8]}";

    private static async Task<JsonElement> LoginAsync(HttpClient client, Guid environmentId, string phone)
    {
        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { environmentId, phonePrimary = phone, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> ReadCentersAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("centers").EnumerateArray().ToList();
    }

    private static async Task<Guid> SeedUserAsync(PgTestScope scope, string phone)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField);
        if (role is null)
        {
            role = new Role { EnvironmentId = scope.EnvironmentId, Key = RoleKey.VetField, Name = "Field" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var hasher = new BCryptPasswordHasher();
        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Shared Phone",
            PhonePrimary = phone,
            PasswordHash = hasher.Hash(Password),
            Status = UserStatus.Active,
            NumberPrefix = $"P{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task SetEnvStatusAsync(PgTestScope scope, string status)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var env = await db.Environments.FirstAsync(e => e.Id == scope.EnvironmentId);
        env.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<string> ReadCodeAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.Environments.Where(e => e.Id == scope.EnvironmentId).Select(e => e.Code).FirstAsync();
    }
}
