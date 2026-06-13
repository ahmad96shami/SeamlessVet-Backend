using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Sync;

/// <summary>
/// Mo11 — a field visit created offline under a supervision batch (Dawra) must carry its
/// <c>batch_id</c> through <c>/sync/visits</c>. The link drives the exam-fee-covered-by-batch guard
/// (M28/M30), field-invoice batch attribution, and settlement; the handler previously dropped it
/// (built the visit without reading <c>batch_id</c>/<c>contract_id</c>), so a device's batch pick
/// never reached the server. This pins the persistence + the existence validation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VisitSyncBatchLinkTests
{
    [Fact]
    public async Task SyncVisit_WithBatchId_PersistsTheBatchLink()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "poultry_farm", fullName = "Batch Visit Co",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var batchId = await SeedOpenBatchAsync(scope, customerId, admin.Id);

        var visitId = Guid.CreateVersion7();
        (await SyncPutAsync(client, "visits", new
        {
            id = visitId,
            visit_type = "field",
            customer_id = customerId,
            doctor_id = admin.Id,
            batch_id = batchId,
            status = "open",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var visit = await db.Visits.AsNoTracking().FirstAsync(v => v.Id == visitId);
        visit.BatchId.Should().Be(batchId, "the device's batch (Dawra) pick must reach the server");
    }

    [Fact]
    public async Task SyncVisit_WithUnknownBatchId_IsRejected()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "No Batch Co",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SyncPutAsync(client, "visits", new
        {
            id = Guid.CreateVersion7(),
            visit_type = "field",
            customer_id = customerId,
            doctor_id = admin.Id,
            batch_id = Guid.CreateVersion7(), // does not exist
            status = "open",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> SeedOpenBatchAsync(PgTestScope scope, Guid customerId, Guid doctorId)
    {
        await using var db = NewContext(scope, Guid.Empty);
        var id = Guid.CreateVersion7();
        db.Batches.Add(new Batch
        {
            Id = id,
            EnvironmentId = scope.EnvironmentId,
            CustomerId = customerId,
            ResponsibleDoctorId = doctorId,
            AnimalCount = 500,
            StartDate = new DateOnly(2026, 6, 1),
            SupervisionFeeModel = FeeModel.FixedAmount,
            SupervisionFeeValue = 100m,
            Status = BatchStatus.Open,
        });
        await db.SaveChangesAsync();
        return id;
    }

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
        request.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SyncPutAsync(HttpClient client, string table, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/sync/{table}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
