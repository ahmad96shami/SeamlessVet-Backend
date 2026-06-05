using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Identity;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Devices;

/// <summary>
/// M21 task 4 — <c>/devices/push-token</c> (+ <c>/unregister</c>): auth-only self-scoped writes.
/// Register inserts then upserts BY TOKEN (a shared device re-registering under another user is
/// reassigned, never duplicated — the plain-POCO modeling exists exactly for this); unregister
/// removes the caller's own row, no-ops on someone else's, and is idempotent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeviceTokensIntegrationTests
{
    [Fact]
    public async Task Register_inserts_then_reregistering_the_same_token_reassigns_the_user()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedActiveUserAsync(scope);
        await using var factory = new VetApiFactory();
        var token = UniqueToken("shared-device");

        using (var adminClient = AuthedClient(factory, admin.Id, admin.EnvironmentId))
        {
            (await adminClient.PostAsJsonAsync("/devices/push-token", new { token, platform = "android" }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var row = await SingleTokenAsync(scope, token);
        row.UserId.Should().Be(admin.Id);
        row.Platform.Should().Be("android");
        var firstSeen = row.LastSeenAt;

        // The same physical device, now signed in as the doctor → same row, new owner.
        using (var doctorClient = AuthedClient(factory, doctorId, scope.EnvironmentId))
        {
            (await doctorClient.PostAsJsonAsync("/devices/push-token", new { token, platform = "android" }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        row = await SingleTokenAsync(scope, token);
        row.UserId.Should().Be(doctorId);
        row.LastSeenAt.Should().BeOnOrAfter(firstSeen);
    }

    [Fact]
    public async Task Unregister_removes_own_token_noops_on_anothers_and_is_idempotent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedActiveUserAsync(scope);
        await using var factory = new VetApiFactory();
        var adminToken = UniqueToken("admin");
        var doctorToken = UniqueToken("doctor");

        using var adminClient = AuthedClient(factory, admin.Id, admin.EnvironmentId);
        using var doctorClient = AuthedClient(factory, doctorId, scope.EnvironmentId);
        await adminClient.PostAsJsonAsync("/devices/push-token", new { token = adminToken, platform = "android" });
        await doctorClient.PostAsJsonAsync("/devices/push-token", new { token = doctorToken, platform = "android" });

        // The doctor cannot remove the admin's device — silent no-op, not an error (idempotent contract).
        (await doctorClient.PostAsJsonAsync("/devices/push-token/unregister", new { token = adminToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TokenExistsAsync(scope, adminToken)).Should().BeTrue();

        (await adminClient.PostAsJsonAsync("/devices/push-token/unregister", new { token = adminToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TokenExistsAsync(scope, adminToken)).Should().BeFalse();

        // Re-unregistering the now-gone token still succeeds.
        (await adminClient.PostAsJsonAsync("/devices/push-token/unregister", new { token = adminToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Requires_authentication_and_validates_the_body()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.PostAsJsonAsync("/devices/push-token", new { token = UniqueToken("x"), platform = "android" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var client = AuthedClient(factory, admin.Id, admin.EnvironmentId);
        (await client.PostAsJsonAsync("/devices/push-token", new { token = "", platform = "android" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/devices/push-token", new { token = UniqueToken("x"), platform = "windows" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ----

    private static string UniqueToken(string label)
        => $"ExponentPushToken[{label}-{Guid.NewGuid().ToString("N")[..8]}]";

    private static HttpClient AuthedClient(VetApiFactory factory, Guid userId, Guid environmentId)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(userId, environmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<DeviceToken> SingleTokenAsync(PgTestScope scope, string token)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.DeviceTokens.SingleAsync(t => t.Token == token);
    }

    private static async Task<bool> TokenExistsAsync(PgTestScope scope, string token)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.DeviceTokens.AnyAsync(t => t.Token == token);
    }

    private static async Task<Guid> SeedActiveUserAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Field Doctor",
            PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
