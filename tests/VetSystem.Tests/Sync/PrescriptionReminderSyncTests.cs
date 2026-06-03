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

namespace VetSystem.Tests.Sync;

/// <summary>
/// Mobile Mo9 hygiene — SQLite has no boolean type, so a PowerSync device stores
/// <c>reminder_enabled</c> as a 0/1 INTEGER and the upload connector replays the row verbatim:
/// the M18 reminder flag arrives at <c>/sync/prescriptions</c> as a JSON <b>number</b>, not a
/// boolean. <see cref="VetSystem.API.Endpoints.Sync.SyncBody"/> must accept exactly 0/1 there
/// (and still reject anything else as <c>invalid_payload</c>).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PrescriptionReminderSyncTests
{
    [Fact]
    public async Task ReminderFlag_AsSqliteZeroOne_RoundTripsThroughSync()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedProductAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Reminder Owner",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "field", customerId, doctorId = admin.Id, status = "in_progress",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Device-authored PUT: reminder_enabled is the SQLite integer 1, never a JSON true.
        var rxId = Guid.CreateVersion7();
        var startAt = DateTimeOffset.UtcNow.AddHours(1);
        (await SyncPutAsync(client, "prescriptions", new
        {
            id = rxId,
            visit_id = visitId,
            product_id = productId,
            dispense_type = DispenseType.DispensedToOwner,
            quantity = 2m,
            reminder_enabled = 1,
            interval_minutes = 480,
            start_at = startAt,
            doses_count = 6,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var rx = await db.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == rxId);
            rx.ReminderEnabled.Should().BeTrue("the device's 0/1 integer flag must coerce to a bool");
            rx.IntervalMinutes.Should().Be(480);
            rx.DosesCount.Should().Be(6);
        }

        // Retuning the schedule off via PATCH uses the same 0/1 shape.
        (await SyncPatchAsync(client, "prescriptions", rxId, new { reminder_enabled = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == rxId))
                .ReminderEnabled.Should().BeFalse();
        }

        // Anything other than 0/1/true/false/null is still a malformed payload.
        (await SyncPatchAsync(client, "prescriptions", rxId, new { reminder_enabled = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
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

    private static async Task<HttpResponseMessage> SyncPatchAsync(HttpClient client, string table, Guid id, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/sync/{table}/{id}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> SeedProductAsync(PgTestScope scope)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;
        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "مضاد حيوي",
            Category = ProductCategory.Medication, PurchasePrice = 3m, SellingPrice = 7m,
            CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return productId;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
