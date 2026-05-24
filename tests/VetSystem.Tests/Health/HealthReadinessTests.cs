using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Health;

/// <summary>
/// M13 task 15 — the deep readiness probe. Against a real host + Postgres container in the Test
/// environment: the database is reachable (fatal check passes), Hangfire is "disabled" (off in Test),
/// and the PowerSync logical slot is "missing" (no PowerSync service runs in tests). The probe stays
/// 200 with <c>status: "degraded"</c> — only an unreachable database would make it 503.
/// </summary>
[Trait("Category", "Integration")]
public sealed class HealthReadinessTests
{
    [Fact]
    public async Task Ready_ReportsEachDeepCheck_AndStaysHealthyWhileDbIsReachable()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checks = body.GetProperty("checks");
        checks.GetProperty("database").GetString().Should().Be("ok");
        checks.GetProperty("hangfire").GetString().Should().Be("disabled");
        checks.GetProperty("powersync").GetString().Should().Be("missing");
        body.GetProperty("status").GetString().Should().Be("degraded");
    }
}
