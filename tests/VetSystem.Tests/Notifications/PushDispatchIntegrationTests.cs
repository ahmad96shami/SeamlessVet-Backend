using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Identity;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M21 task 3 — a real <c>DispatchAsync</c> fans out to the push worker: every registered device of
/// every recipient gets one message (a bystander's device gets none), the deeplink <c>data</c> is
/// EXACTLY the SignalR shape with the recipient's own per-row notification id, and a token the
/// provider reports dead is pruned from <c>device_tokens</c>. The worker is async (channel +
/// hosted service), so assertions poll with a timeout like the hub tests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PushDispatchIntegrationTests
{
    [Fact]
    public async Task Dispatch_pushes_to_each_recipients_devices_with_the_signalr_data_shape()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedActiveUserAsync(scope);
        var bystanderId = await SeedActiveUserAsync(scope);

        // Tokens are GLOBALLY unique and the scope teardown only deletes its environments row —
        // randomize per run (the same reason user seeding randomizes phone numbers).
        var adminPhone = UniqueToken("admin-phone");
        var adminTablet = UniqueToken("admin-tablet");
        var doctorPhone = UniqueToken("doctor");

        // Admin carries two devices (phone + tablet); the bystander's token must stay untouched.
        await SeedDeviceTokenAsync(scope, admin.Id, adminPhone);
        await SeedDeviceTokenAsync(scope, admin.Id, adminTablet);
        await SeedDeviceTokenAsync(scope, doctorId, doctorPhone);
        await SeedDeviceTokenAsync(scope, bystanderId, UniqueToken("bystander"));

        var sender = new RecordingPushSender();
        await using var factory = new VetApiFactory { PushSender = sender };

        using (var s = factory.Services.CreateScope())
        {
            var dispatcher = s.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                new NotificationDispatch(scope.EnvironmentId, [admin.Id, doctorId], NotificationType.LowStock,
                    "تنبيه مخزون", "الكمية منخفضة", new { ProductId = Guid.CreateVersion7() }),
                CancellationToken.None);
        }

        await WaitUntilAsync(() => Task.FromResult(sender.Messages.Count >= 3),
            "expected 3 push messages (2 admin devices + 1 doctor device)");

        sender.Messages.Select(m => m.Token).Should().BeEquivalentTo(adminPhone, adminTablet, doctorPhone);

        // The data shape the mobile router consumes from BOTH channels — keys verbatim.
        var rowIdByUser = await NotificationIdsByUserAsync(scope, NotificationType.LowStock);
        foreach (var message in sender.Messages)
        {
            message.Title.Should().Be("تنبيه مخزون");
            message.Body.Should().Be("الكمية منخفضة");
            message.Data.Keys.Should().BeEquivalentTo("notificationId", "type", "payload");
            message.Data["type"].Should().Be(NotificationType.LowStock);
            ((JsonElement)message.Data["payload"]!).TryGetProperty("ProductId", out _).Should().BeTrue(
                "the payload rides along as the stored jsonb (PascalCase — mobile matches case-insensitively)");
        }

        // Each device's deeplink carries its OWNER's feed-row id, not a shared one.
        var adminMessages = sender.Messages.Where(m => m.Token == adminPhone || m.Token == adminTablet);
        adminMessages.Should().OnlyContain(m => (Guid)m.Data["notificationId"]! == rowIdByUser[admin.Id]);
        sender.Messages.Single(m => m.Token == doctorPhone)
            .Data["notificationId"].Should().Be(rowIdByUser[doctorId]);
    }

    [Fact]
    public async Task Tokens_the_provider_reports_dead_are_pruned()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var deadToken = UniqueToken("dead");
        var aliveToken = UniqueToken("alive");
        await SeedDeviceTokenAsync(scope, admin.Id, deadToken);
        await SeedDeviceTokenAsync(scope, admin.Id, aliveToken);

        var sender = new RecordingPushSender { DeadTokens = [deadToken] };
        await using var factory = new VetApiFactory { PushSender = sender };

        using (var s = factory.Services.CreateScope())
        {
            var dispatcher = s.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                new NotificationDispatch(scope.EnvironmentId, [admin.Id], NotificationType.ExpiryWarning,
                    "تنبيه", null, null),
                CancellationToken.None);
        }

        await WaitUntilAsync(async () => !await TokenExistsAsync(scope, deadToken),
            "the DeviceNotRegistered token should be hard-deleted by the push worker");
        (await TokenExistsAsync(scope, aliveToken)).Should().BeTrue();
    }

    // ---- helpers ----

    private static string UniqueToken(string label)
        => $"ExponentPushToken[{label}-{Guid.NewGuid().ToString("N")[..8]}]";

    private static async Task<Guid> SeedActiveUserAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField);

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

    private static async Task SeedDeviceTokenAsync(PgTestScope scope, Guid userId, string token)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        // Plain POCO — no auditing interceptor, so every column is set explicitly.
        db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EnvironmentId = scope.EnvironmentId,
            Token = token,
            Platform = "android",
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<bool> TokenExistsAsync(PgTestScope scope, string token)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.DeviceTokens.AnyAsync(t => t.Token == token);
    }

    private static async Task<Dictionary<Guid, Guid>> NotificationIdsByUserAsync(PgTestScope scope, string type)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EnvironmentId == scope.EnvironmentId && n.Type == type)
            .ToDictionaryAsync(n => n.UserId, n => n.Id);
    }

    /// <summary>The push worker is async — poll like the hub tests await their TCS, capped at 10s.</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(because);
    }

    private sealed class RecordingPushSender : IPushSender
    {
        private readonly List<PushMessage> _messages = [];

        public IReadOnlyCollection<string> DeadTokens { get; init; } = [];

        public IReadOnlyList<PushMessage> Messages
        {
            get { lock (_messages) { return [.. _messages]; } }
        }

        public Task<IReadOnlyCollection<string>> SendAsync(
            IReadOnlyCollection<PushMessage> messages, CancellationToken cancellationToken)
        {
            lock (_messages) { _messages.AddRange(messages); }
            return Task.FromResult(DeadTokens);
        }
    }
}
