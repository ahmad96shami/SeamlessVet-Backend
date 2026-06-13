using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Identity;
using VetSystem.Application.Common;
using VetSystem.Application.Identity;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

/// <summary>
/// M32 — the live tenant-suspension gate. <c>EnvironmentStatusMiddleware</c> rejects authenticated
/// tenant requests whose environment is suspended/deleted (on already-issued JWTs), and
/// <c>IEnvironmentStatusProvider.Invalidate</c> makes a console suspend bite within one request.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EnvironmentSuspensionTests
{
    [Fact]
    public async Task Active_environment_request_passes_the_gate()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        using var client = AuthedClient(factory, admin.Id, admin.EnvironmentId);
        var response = await client.PostAsync("/auth/powersync-token", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspended_environment_rejects_an_already_issued_token()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        // Suspend before the first request so the status cache is cold and reads 'suspended' fresh.
        await SetStatusAsync(scope, EnvironmentStatus.Suspended);

        using var client = AuthedClient(factory, admin.Id, admin.EnvironmentId);
        var response = await client.PostAsync("/auth/powersync-token", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("environment_suspended");
    }

    [Fact]
    public async Task Invalidate_makes_a_suspend_take_effect_within_the_cache_window()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        using var client = AuthedClient(factory, admin.Id, admin.EnvironmentId);

        // First request caches 'active' for this env.
        (await client.PostAsync("/auth/powersync-token", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Suspend in the DB, then invalidate the cache the way the platform console will.
        await SetStatusAsync(scope, EnvironmentStatus.Suspended);
        using (var s = factory.Services.CreateScope())
        {
            s.ServiceProvider.GetRequiredService<IEnvironmentStatusProvider>()
                .Invalidate(scope.EnvironmentId);
        }

        (await client.PostAsync("/auth/powersync-token", content: null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Provider_returns_null_for_a_soft_deleted_environment()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await SoftDeleteEnvAsync(scope);

        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new EnvironmentStatusProvider(db, cache);

        var status = await provider.GetStatusAsync(scope.EnvironmentId, CancellationToken.None);
        status.Should().BeNull("a soft-deleted environment must read as gone, not active");
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, Guid userId, Guid environmentId)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(userId, environmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task SetStatusAsync(PgTestScope scope, string status)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var env = await db.Environments.FirstAsync(e => e.Id == scope.EnvironmentId);
        env.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task SoftDeleteEnvAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var env = await db.Environments.FirstAsync(e => e.Id == scope.EnvironmentId);
        env.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
