using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Farms;

/// <summary>
/// M15 exit criteria, against a real Postgres env:
/// <list type="bullet">
/// <item>a field doctor offline creates a farm for their customer; it syncs via <c>/sync/farms</c>
///       last-write-wins with no server overwrite (mirrors pets);</item>
/// <item>a contract attaches to ≥2 farms of the same customer; attaching a farm of a <i>different</i>
///       customer is rejected.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class FarmsIntegrationTests
{
    [Fact]
    public async Task Farm_CreatedAndEditedViaSync_RoundTrips_WithoutServerOverwrite()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        // Device authors a farm offline → uploads via /sync/farms (snake_case payload).
        var farmId = Guid.CreateVersion7();
        (await SyncPutAsync(client, "farms", new
        {
            id = farmId,
            customer_id = customerId,
            name = "حظيرة الشمال",
            kind = FarmKind.Poultry,
            head_count = 5000,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Device edits the farm offline → uploads the change; the server accepts it (last-write-wins).
        (await SyncPatchAsync(client, "farms", farmId, new { head_count = 5200, location = "جنين" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var farm = await db.Farms.AsNoTracking().FirstAsync(f => f.Id == farmId);
            farm.CustomerId.Should().Be(customerId);
            farm.Kind.Should().Be(FarmKind.Poultry);
            farm.HeadCount.Should().Be(5200, "the offline edit synced without server overwrite");
            farm.Location.Should().Be("جنين");
        }

        // Re-PUT of the same id is rejected (use PATCH to update) — mirrors pets.
        (await SyncPutAsync(client, "farms", new { id = farmId, customer_id = customerId, name = "x", kind = FarmKind.Other }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // And the farm is readable through the REST surface.
        var get = await client.GetAsync($"/farms/{farmId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Contract_AttachesMultipleFarmsOfSameCustomer_RejectsCrossCustomerFarm()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var farmA = await CreateFarmAsync(client, customerId, "مزرعة أ");
        var farmB = await CreateFarmAsync(client, customerId, "مزرعة ب");

        // A different customer with its own farm.
        var otherCustomerId = await CreateCustomerAsync(client);
        var otherFarm = await CreateFarmAsync(client, otherCustomerId, "مزرعة غريبة");

        // Draft contract owned by the first customer.
        var contractId = Guid.CreateVersion7();
        (await PostAsync(client, "/contracts", new
        {
            id = contractId,
            customerId,
            responsibleDoctorId = admin.Id,
            periodStart = "2026-01-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Attach two farms of the SAME customer → both accepted.
        (await PostAsync(client, $"/contracts/{contractId}/farms", new { id = Guid.CreateVersion7(), farmId = farmA }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/contracts/{contractId}/farms", new { id = Guid.CreateVersion7(), farmId = farmB }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A farm of a DIFFERENT customer is rejected.
        (await PostAsync(client, $"/contracts/{contractId}/farms", new { id = Guid.CreateVersion7(), farmId = otherFarm }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var db = NewContext(scope, admin.Id))
        {
            var attached = await db.ContractFarms.AsNoTracking()
                .Where(cf => cf.ContractId == contractId)
                .Select(cf => cf.FarmId)
                .ToListAsync();
            attached.Should().BeEquivalentTo(new[] { farmA, farmB },
                "only same-customer farms attach; the cross-customer attempt wrote nothing");
        }

        // Detach one farm → soft-deleted, leaving one attachment.
        (await DeleteAsync(client, $"/contracts/{contractId}/farms/{farmA}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.ContractFarms.AsNoTracking().CountAsync(cf => cf.ContractId == contractId))
                .Should().Be(1, "the detached farm was soft-deleted");
        }
    }

    [Fact]
    public async Task ContractFarm_AttachViaSync_LockedOnceContractActive()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var farmId = await CreateFarmAsync(client, customerId, "مزرعة العقد");

        var contractId = Guid.CreateVersion7();
        (await PostAsync(client, "/contracts", new
        {
            id = contractId,
            customerId,
            responsibleDoctorId = admin.Id,
            periodStart = "2026-01-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // While draft, the device can attach a farm via /sync/contract_farms.
        var draftAttach = Guid.CreateVersion7();
        (await SyncPutAsync(client, "contract_farms", new { id = draftAttach, contract_id = contractId, farm_id = farmId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Activate the contract → coverage becomes server-authoritative.
        (await PostAsync(client, $"/contracts/{contractId}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A late offline detach of the now-active contract's farm is rejected.
        (await SyncDeleteAsync(client, "contract_farms", draftAttach)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, User user, string role = "admin")
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, role));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SyncPutAsync(HttpClient client, string table, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/sync/{table}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SyncPatchAsync(HttpClient client, string table, Guid id, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/sync/{table}/{id}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SyncDeleteAsync(HttpClient client, string table, Guid id)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/sync/{table}/{id}");
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "poultry_farm",
            fullName = "Farm Owner",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client, Guid customerId, string name)
    {
        var farmId = Guid.CreateVersion7();
        (await PostAsync(client, "/farms", new
        {
            id = farmId,
            customerId,
            name,
            kind = FarmKind.Poultry,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return farmId;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
