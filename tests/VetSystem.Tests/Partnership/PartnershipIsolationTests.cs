using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Partnership;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Partnership;

/// <summary>
/// M10 task 10 + exit criteria — cross-environment isolation for the partnership surface, verified
/// across every read path: the REST endpoints, the env-scoped EF query filter, and the
/// profit-distribution service that M12 reports will consume. Plus the task-7 hard guard that refuses
/// to persist a syncable row with no environment.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PartnershipIsolationTests
{
    [Fact]
    public async Task PartnerAndShareEndpoints_DoNotLeakAcrossEnvironments()
    {
        await using var envA = await PgTestScope.CreateAsync("partnership");
        await using var envB = await PgTestScope.CreateAsync("partnership");
        var adminA = await AdminTestSeed.SeedAdminAsync(envA);
        var adminB = await AdminTestSeed.SeedAdminAsync(envB);

        await using var factory = new VetApiFactory();
        using var clientA = AuthedClient(factory, adminA);
        using var clientB = AuthedClient(factory, adminB);

        var partnerA = await CreatePartnerAsync(clientA, "Partner A");
        var shareA = await CreateShareAsync(clientA, partnerA, 40m, "2026-01-01");
        var partnerB = await CreatePartnerAsync(clientB, "Partner B");
        await CreateShareAsync(clientB, partnerB, 50m, "2026-01-01");

        // List is environment-scoped: A sees only A's partner, never B's.
        var listA = await GetJsonAsync<List<PartnerRow>>(clientA, "/partners");
        listA.Select(p => p.Id).Should().Contain(partnerA).And.NotContain(partnerB);

        // A cannot fetch B's partner by id — the env filter makes it a 404, not a 403.
        (await clientA.GetAsync($"/partners/{partnerB}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A cannot edit or delete B's partner either (still env-filtered → 404).
        (await PatchAsync(clientA, $"/partners/{partnerB}", new { displayName = "hijack" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DeleteAsync(clientA, $"/partners/{partnerB}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Shares list is likewise scoped.
        var sharesA = await GetJsonAsync<List<ShareRow>>(clientA, "/partnership-shares");
        sharesA.Select(s => s.Id).Should().Contain(shareA);
        sharesA.Should().OnlyContain(s => s.PartnerId == partnerA);

        // DB-level env filter: an A-scoped context never materializes B's rows.
        await using (var dbA = NewContext(envA, adminA.Id))
        {
            (await dbA.Partners.Select(p => p.Id).ToListAsync()).Should().Contain(partnerA).And.NotContain(partnerB);
            (await dbA.PartnershipShares.AnyAsync(s => s.PartnerId == partnerB)).Should().BeFalse();
        }

        // The profit-distribution service (M12's report source) resolves only the current env's shares.
        await using (var dbA = NewContext(envA, adminA.Id))
        {
            var svc = new ProfitDistributionService(dbA);
            var resolved = await svc.ResolveSharesAsync(envA.EnvironmentId, new DateOnly(2026, 6, 1), default);
            resolved.Should().ContainSingle().Which.PartnerId.Should().Be(partnerA);
        }
    }

    [Fact]
    public async Task SoloEnvironment_PartnerEndpoints_Return404()
    {
        await using var solo = await PgTestScope.CreateAsync("solo");
        var admin = await AdminTestSeed.SeedAdminAsync(solo);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        (await client.GetAsync("/partners")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, "/partners", new { displayName = "Nope" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/partnership-shares")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SaveChanges_RejectsEntityWithNoEnvironment()
    {
        // The interceptor is only wired through the real DI graph, so resolve the context from the host
        // (a bare PgTestScope context skips it). Outside a request there is no current user, so no
        // environment can be inherited; with none set on the row, the hardened
        // AuditingSaveChangesInterceptor (task 7) must refuse the write before it reaches Postgres.
        await using var factory = new VetApiFactory();
        using var hostScope = factory.Services.CreateScope();
        var db = hostScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Partners.Add(new Partner { DisplayName = "Orphan" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EnvironmentId*");
    }

    // ---- helpers ----

    private sealed record PartnerRow(Guid Id, string DisplayName);
    private sealed record ShareRow(Guid Id, Guid PartnerId, decimal SharePercent);

    private static HttpClient AuthedClient(VetApiFactory factory, User user)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<Guid> CreatePartnerAsync(HttpClient client, string name)
    {
        var id = Guid.CreateVersion7();
        (await PostAsync(client, "/partners", new { id, displayName = name }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    private static async Task<Guid> CreateShareAsync(HttpClient client, Guid partnerId, decimal pct, string from)
    {
        var id = Guid.CreateVersion7();
        (await PostAsync(client, "/partnership-shares", new { id, partnerId, sharePercent = pct, effectiveFrom = from }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
