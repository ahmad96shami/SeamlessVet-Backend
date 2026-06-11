using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Identity;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M11 task 17 + exit criterion — the hub authenticates via JWT and joins per-user / admin groups on
/// connect, and the dispatcher's push reaches a connected client within seconds. Connections run over
/// long-polling against the in-memory test server (the query-string token path used by real
/// WebSocket clients is covered by the live smoke).
/// </summary>
[Trait("Category", "Integration")]
public sealed class NotificationsHubIntegrationTests
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Connect_without_a_token_is_rejected()
    {
        await using var factory = new VetApiFactory();
        await using var connection = BuildConnection(factory, accessToken: null);

        var connect = async () => await connection.StartAsync();

        await connect.Should().ThrowAsync<Exception>("the hub requires an authenticated principal");
    }

    [Fact]
    public async Task Connected_user_receives_a_push_to_its_user_group()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        var received = new TaskCompletionSource<NotificationPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = BuildConnection(factory, AccessToken(factory, admin));
        connection.On<NotificationPayload>(nameof(INotificationClient.ReceiveNotification), n => received.TrySetResult(n));
        await connection.StartAsync();

        using (var s = factory.Services.CreateScope())
        {
            var dispatcher = s.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                new NotificationDispatch(scope.EnvironmentId, [admin.Id], NotificationType.EntitlementCredited,
                    "تقييد", "تم تقييد الاستحقاق", new { amount = 65m }),
                CancellationToken.None);
        }

        var payload = await AwaitWithTimeout(received.Task);
        payload.Type.Should().Be(NotificationType.EntitlementCredited);
    }

    [Fact]
    public async Task Admin_connection_joins_the_environment_admins_group()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();

        var received = new TaskCompletionSource<NotificationPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = BuildConnection(factory, AccessToken(factory, admin));
        connection.On<NotificationPayload>(nameof(INotificationClient.ReceiveNotification), n => received.TrySetResult(n));
        await connection.StartAsync();

        using (var s = factory.Services.CreateScope())
        {
            var hub = s.ServiceProvider.GetRequiredService<IHubContext<NotificationsHub, INotificationClient>>();
            await hub.Clients
                .Group(NotificationGroups.Admins(admin.EnvironmentId.ToString()))
                .ReceiveNotification(new NotificationPayload(
                    Guid.CreateVersion7(), NotificationType.ReportDelivery, "t", "b", null, DateTimeOffset.UtcNow, null));
        }

        var payload = await AwaitWithTimeout(received.Task);
        payload.Type.Should().Be(NotificationType.ReportDelivery);
    }

    // ---- helpers ----

    private static HubConnection BuildConnection(VetApiFactory factory, string? accessToken)
        => new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();

    private static string AccessToken(VetApiFactory factory, User user)
        => factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, "admin")).Token;

    private static async Task<NotificationPayload> AwaitWithTimeout(Task<NotificationPayload> task)
    {
        var winner = await Task.WhenAny(task, Task.Delay(ReceiveTimeout));
        winner.Should().Be(task, "the connected client should receive the push before the timeout");
        return await task;
    }
}
