using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Sync;

/// <summary>
/// M13 task 10 — the per-user token-bucket rate limit on <c>/sync/*</c> (PRD §14: absorb field-doctor
/// reconnect/sync storms). The limiter is off in the Test environment by default so the broad suite is
/// never throttled; here it is switched on with a tiny bucket (<c>SyncTokenLimit = 3</c>, long
/// replenishment) so a single burst is deterministic: of 5 rapid requests from one user exactly the
/// 2 over the bucket are rejected with 429 + the canonical <c>rate_limited</c> error shape.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SyncRateLimitTests
{
    private sealed record ErrorShape(string? Code, string? Message);

    [Fact]
    public async Task Sync_OverTheTokenBucket_Returns429_WithCanonicalError()
    {
        const int tokenLimit = 3;
        const int burst = 5;

        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory { EnableRateLimiting = true, SyncTokenLimit = tokenLimit };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var statuses = new List<HttpStatusCode>();
        ErrorShape? rejection = null;
        var retryAfterSeen = false;

        // Same user → same partition → same bucket. Sequential so the count is deterministic.
        for (var i = 0; i < burst; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"/sync/test/{Guid.CreateVersion7()}");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                retryAfterSeen |= response.Headers.Contains("Retry-After");
                rejection = await response.Content.ReadFromJsonAsync<ErrorShape>();
            }
        }

        statuses.Count(s => s == HttpStatusCode.TooManyRequests)
            .Should().Be(burst - tokenLimit, "a 3-token bucket admits 3 of 5 rapid requests and rejects the rest");
        rejection.Should().NotBeNull();
        rejection!.Code.Should().Be("rate_limited");
        retryAfterSeen.Should().BeTrue("a token-bucket rejection advertises when to retry");
    }
}
