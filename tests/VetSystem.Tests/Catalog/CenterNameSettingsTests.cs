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
/// The manager renames their center from the one Settings screen: <c>PATCH /admin/settings</c> with
/// a <c>CenterName</c> updates the owning <c>environments</c> row (the same name shown in the login
/// picker, the shell, and every printed document header), and the value round-trips on GET. A blank
/// name is rejected — a center must keep a name.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CenterNameSettingsTests
{
    [Fact]
    public async Task PatchSettings_WithCenterName_RenamesEnvironment_AndRoundTripsOnGet()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        const string newName = "مركز رام الله البيطري";

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/admin/settings")
        {
            Content = JsonContent.Create(new { CenterName = newName }),
        };
        patch.Headers.Add("Idempotency-Key", $"center-{Guid.NewGuid():N}"[..32]);
        (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.OK);

        // GET surfaces the new name from the environments row.
        var get = await client.GetFromJsonAsync<JsonElement>("/admin/settings");
        get.GetProperty("centerName").GetString().Should().Be(newName);

        // The environments row itself was renamed (what the login picker + shell read).
        await using var db = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true, EnvironmentId = scope.EnvironmentId, UserId = admin.Id,
        });
        var envName = await db.Environments.IgnoreQueryFilters()
            .Where(e => e.Id == scope.EnvironmentId).Select(e => e.Name).FirstAsync();
        envName.Should().Be(newName);
    }

    [Fact]
    public async Task PatchSettings_WithBlankCenterName_IsRejected()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/admin/settings")
        {
            Content = JsonContent.Create(new { CenterName = "   " }),
        };
        patch.Headers.Add("Idempotency-Key", $"center-blank-{Guid.NewGuid():N}"[..32]);
        (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a center must keep a name — a blank rename is invalid");
    }
}
