using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Identity;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M11 tasks 7, 8, 12–14 — domain-event handlers turn events into persisted notifications for the
/// right recipients, and the in-app feed lists/marks them. Events are published through the real
/// registered <see cref="IDomainEventPublisher"/> (the dispatching publisher + its handlers) so the
/// fresh-scope dispatch, recipient resolution, and persistence are all exercised end to end.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NotificationsIntegrationTests
{
    [Fact]
    public async Task NegativeStockEvent_notifies_the_acting_doctor_and_admins()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedActiveUserAsync(scope, RoleKey.VetField);
        await using var factory = new VetApiFactory();

        var publisher = factory.Services.GetRequiredService<IDomainEventPublisher>();
        await publisher.PublishAsync(
            new NegativeStockAttemptedEvent(
                scope.EnvironmentId, ProductId: Guid.CreateVersion7(), LocationType: StockLocation.Field,
                LocationId: Guid.CreateVersion7(), AttemptedDelta: -5m, CurrentQuantity: 2m,
                PerformedBy: doctorId, VisitId: null),
            CancellationToken.None);

        var recipients = await RecipientsOfTypeAsync(scope, NotificationType.NegativeStock);
        recipients.Should().BeEquivalentTo([admin.Id, doctorId]);
    }

    [Fact]
    public async Task AccountReadyForSettlementEvent_notifies_admins()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        var publisher = factory.Services.GetRequiredService<IDomainEventPublisher>();
        await publisher.PublishAsync(
            new AccountReadyForSettlementEvent(scope.EnvironmentId, Guid.CreateVersion7(), null, Guid.CreateVersion7(), 100m),
            CancellationToken.None);

        var recipients = await RecipientsOfTypeAsync(scope, NotificationType.AccountReadyForSettlement);
        recipients.Should().Contain(admin.Id);
    }

    [Fact]
    public async Task EntitlementCreditedEvent_notifies_only_the_doctor()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedActiveUserAsync(scope, RoleKey.VetField);
        await using var factory = new VetApiFactory();

        var publisher = factory.Services.GetRequiredService<IDomainEventPublisher>();
        await publisher.PublishAsync(
            new EntitlementCreditedEvent(scope.EnvironmentId, Guid.CreateVersion7(), doctorId, Guid.CreateVersion7(), 65m),
            CancellationToken.None);

        var recipients = await RecipientsOfTypeAsync(scope, NotificationType.EntitlementCredited);
        recipients.Should().BeEquivalentTo([doctorId]);
    }

    [Fact]
    public async Task Feed_returns_own_notifications_and_mark_read_is_idempotent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        using (var s = factory.Services.CreateScope())
        {
            var dispatcher = s.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                new NotificationDispatch(scope.EnvironmentId, [admin.Id], NotificationType.LowStock,
                    "تنبيه", "نص", new { count = 3 }),
                CancellationToken.None);
        }

        using var client = AuthedClient(factory, admin);

        var feed = await client.GetFromJsonAsync<List<JsonElement>>("/notifications") ?? [];
        feed.Should().HaveCount(1);
        feed[0].GetProperty("type").GetString().Should().Be(NotificationType.LowStock);
        feed[0].GetProperty("readAt").ValueKind.Should().Be(JsonValueKind.Null);
        var notificationId = feed[0].GetProperty("id").GetGuid();

        (await client.PostAsync($"/notifications/{notificationId}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // Idempotent: a second mark-read still succeeds.
        (await client.PostAsync($"/notifications/{notificationId}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var unread = await client.GetFromJsonAsync<List<JsonElement>>("/notifications?unreadOnly=true") ?? [];
        unread.Should().BeEmpty();
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, User user)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<List<Guid>> RecipientsOfTypeAsync(PgTestScope scope, string type)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EnvironmentId == scope.EnvironmentId && n.Type == type)
            .Select(n => n.UserId)
            .ToListAsync();
    }

    private static async Task<Guid> SeedActiveUserAsync(PgTestScope scope, string roleKey)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Field Doctor",
            PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
