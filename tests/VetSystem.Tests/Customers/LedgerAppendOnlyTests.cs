using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VetSystem.API.Endpoints.Sync;
using VetSystem.Application.Common;
using VetSystem.Application.Ledgers;
using VetSystem.Domain.Common;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Customers;

/// <summary>
/// M3 task 13 — append-only enforcement on <c>/sync/ledger_entries</c>. Covers both the
/// in-process handler (fast direct guard) and the live HTTP pipeline (the wire-format clients
/// actually hit). SCHEMA "Key invariants" #3: ledger entries are corrected by adding new
/// <c>adjustment</c> rows, never by updating or deleting existing ones.
/// </summary>
public sealed class LedgerAppendOnlyTests
{
    [Fact]
    public async Task Handler_PatchAsync_ThrowsAppendOnly()
    {
        var handler = new LedgerEntriesSyncHandler(Mock.Of<ILedgerService>());

        var act = () => handler.PatchAsync(Guid.NewGuid(), default, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ledger_entries_append_only");
    }

    [Fact]
    public async Task Handler_DeleteAsync_ThrowsAppendOnly()
    {
        var handler = new LedgerEntriesSyncHandler(Mock.Of<ILedgerService>());

        var act = () => handler.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ledger_entries_append_only");
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task Sync_PatchLedgerEntries_Returns409WithAppendOnlyCode()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/sync/ledger_entries/{Guid.CreateVersion7()}")
        {
            Content = JsonContent.Create(new { amount = 50.0m }),
        };
        patch.Headers.Add("Idempotency-Key", $"patch-{Guid.NewGuid():N}"[..32]);

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "ledger_entries must be append-only on every write path");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("ledger_entries_append_only");
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task Sync_DeleteLedgerEntries_Returns409WithAppendOnlyCode()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/sync/ledger_entries/{Guid.CreateVersion7()}");
        delete.Headers.Add("Idempotency-Key", $"delete-{Guid.NewGuid():N}"[..32]);

        var response = await client.SendAsync(delete);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("ledger_entries_append_only");
    }
}
