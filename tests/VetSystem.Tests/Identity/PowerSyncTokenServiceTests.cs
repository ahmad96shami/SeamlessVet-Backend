using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VetSystem.API.Identity;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

/// <summary>
/// M36 — the PowerSync token must carry the tenant so Sync Rules can scope every stream to one
/// environment. The env is emitted two ways for accessor robustness against the edition-3 token
/// model: a top-level <c>environment_id</c> claim (<c>auth.jwt() -&gt;&gt; 'environment_id'</c>) and a
/// trusted <c>parameters</c> object (<c>auth.parameters() -&gt;&gt; 'environment_id'</c>, which
/// <c>sync-rules.yaml</c> uses). This test reads the raw payload so it proves the <c>parameters</c>
/// claim is a real JSON object, not a stringified blob.
/// </summary>
public sealed class PowerSyncTokenServiceTests
{
    private static PowerSyncTokenService CreateService() => new(
        Options.Create(new PowerSyncOptions()),
        new FakeClock(DateTimeOffset.UtcNow),
        NullLogger<PowerSyncTokenService>.Instance);

    [Fact]
    public void IssueToken_EmbedsEnvironmentId_TopLevelAndUnderParameters()
    {
        var userId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();

        var result = CreateService().IssueToken(userId, environmentId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        using var payload = JsonDocument.Parse(jwt.Payload.SerializeToJson());
        var root = payload.RootElement;

        root.GetProperty("sub").GetString().Should().Be(userId.ToString(),
            "auth.user_id() resolves the subject claim");

        root.GetProperty("environment_id").GetString().Should().Be(environmentId.ToString(),
            "the top-level claim backs the auth.jwt() ->> 'environment_id' accessor");

        root.GetProperty("parameters").ValueKind.Should().Be(JsonValueKind.Object,
            "the parameters claim must be a real JSON object so auth.parameters() can read it");
        root.GetProperty("parameters").GetProperty("environment_id").GetString().Should().Be(environmentId.ToString(),
            "the trusted token-parameter backs the auth.parameters() ->> 'environment_id' accessor used by sync-rules.yaml");
    }

    [Fact]
    public void IssueToken_ScopesDistinctEnvironments_ToDistinctClaims()
    {
        var service = CreateService();
        var userId = Guid.CreateVersion7();
        var envA = Guid.CreateVersion7();
        var envB = Guid.CreateVersion7();

        var tokenA = ReadEnv(service.IssueToken(userId, envA).Token);
        var tokenB = ReadEnv(service.IssueToken(userId, envB).Token);

        tokenA.Should().Be(envA.ToString());
        tokenB.Should().Be(envB.ToString());
        tokenA.Should().NotBe(tokenB, "the same user in two centers must mint distinctly scoped tokens");

        static string? ReadEnv(string token)
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            using var payload = JsonDocument.Parse(jwt.Payload.SerializeToJson());
            return payload.RootElement.GetProperty("parameters").GetProperty("environment_id").GetString();
        }
    }
}
