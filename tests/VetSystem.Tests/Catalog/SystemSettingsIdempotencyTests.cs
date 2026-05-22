using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Catalog;

/// <summary>
/// M2 task 10 — idempotency-key replay on <c>PATCH /admin/settings</c> returns the cached
/// 2xx response and never double-applies the change. Drives the live ASP.NET pipeline through
/// <see cref="VetApiFactory"/> so the <c>IdempotencyKeyFilter</c> + auth filters actually run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SystemSettingsIdempotencyTests
{
    [Fact]
    public async Task PatchSettings_WithSameIdempotencyKey_ReturnsReplayAndDoesNotReapply()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var idempotencyKey = $"settings-replay-{Guid.NewGuid():N}"[..32];

        // 1) First PATCH applies the new exam fee.
        var firstRequest = new HttpRequestMessage(HttpMethod.Patch, "/admin/settings")
        {
            Content = JsonContent.Create(new { DefaultExamFee = 50.00m }),
        };
        firstRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        var firstResponse = await client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var settingsId = firstBody.GetProperty("id").GetGuid();
        settingsId.Should().NotBeEmpty();

        // 2) Second PATCH with the SAME key but a DIFFERENT body must replay, not re-apply.
        var secondRequest = new HttpRequestMessage(HttpMethod.Patch, "/admin/settings")
        {
            Content = JsonContent.Create(new { DefaultExamFee = 999.99m }),
        };
        secondRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        var secondResponse = await client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("id").GetGuid().Should().Be(settingsId);
        secondBody.GetProperty("replay").GetBoolean().Should().BeTrue(
            "the filter must mark the second response as a replay");

        // 3) DB must reflect only the FIRST change — the replay must not re-apply.
        await using var verifyDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var current = await verifyDb.SystemSettings
            .AsNoTracking()
            .FirstAsync(s => s.EnvironmentId == scope.EnvironmentId);

        current.DefaultExamFee.Should().Be(50.00m, "the replay PATCH must not overwrite the first apply");
    }

    [Fact]
    public async Task PatchSettings_DifferentIdempotencyKey_AppliesNewValue()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        await Patch(client, $"keyA-{Guid.NewGuid():N}"[..32], 50m);
        await Patch(client, $"keyB-{Guid.NewGuid():N}"[..32], 75m);

        await using var verifyDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var current = await verifyDb.SystemSettings
            .AsNoTracking()
            .FirstAsync(s => s.EnvironmentId == scope.EnvironmentId);

        current.DefaultExamFee.Should().Be(75m, "a fresh idempotency key must apply the new value");
    }

    private static async Task Patch(HttpClient client, string key, decimal fee)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, "/admin/settings")
        {
            Content = JsonContent.Create(new { DefaultExamFee = fee }),
        };
        req.Headers.Add("Idempotency-Key", key);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"PATCH with key {key} should succeed");
    }
}
