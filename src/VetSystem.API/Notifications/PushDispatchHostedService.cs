using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VetSystem.Application.Notifications;
using VetSystem.Infrastructure.Notifications;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Notifications;

/// <summary>
/// M21 — drains <see cref="PushQueue"/>: per job, resolves the recipients' registered device
/// tokens in a fresh scope, sends one Expo push per device, and hard-deletes tokens the provider
/// reported dead. The push <c>data</c> mirrors the SignalR realtime payload field-for-field
/// (<c>{ notificationId, type, payload }</c>) so the mobile deeplink router serves both channels
/// unchanged. A job failure is logged and dropped — push is best-effort and must never wedge the
/// worker loop.
/// </summary>
public sealed class PushDispatchHostedService : BackgroundService
{
    private readonly PushQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<ExpoPushOptions> _options;
    private readonly ILogger<PushDispatchHostedService> _logger;

    public PushDispatchHostedService(
        PushQueue queue,
        IServiceScopeFactory scopes,
        IOptionsMonitor<ExpoPushOptions> options,
        ILogger<PushDispatchHostedService> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Push job for {NotificationType} in {EnvironmentId} failed; dropped (best-effort)",
                    job.Type, job.EnvironmentId);
            }
        }
    }

    private async Task ProcessAsync(PushJob job, CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // device_tokens is a plain POCO (no env query filter) — scope explicitly, like the
        // recipient resolver does for its no-principal reads.
        var userIds = job.Recipients.Select(r => r.UserId).ToList();
        var tokens = await db.DeviceTokens
            .Where(t => t.EnvironmentId == job.EnvironmentId && userIds.Contains(t.UserId))
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
        {
            return;
        }

        JsonElement? payload = job.PayloadJson is null
            ? null
            : JsonSerializer.Deserialize<JsonElement>(job.PayloadJson);
        var notificationIdByUser = job.Recipients.ToDictionary(r => r.UserId, r => r.NotificationId);

        var messages = tokens.Select(t => new PushMessage(
            Token: t.Token,
            Title: job.Title,
            Body: job.Body,
            Data: new Dictionary<string, object?>
            {
                // EXACTLY the SignalR shape (useNotificationsRealtime) — deeplinks + dedup key on it.
                ["notificationId"] = notificationIdByUser[t.UserId],
                ["type"] = job.Type,
                ["payload"] = payload,
            })).ToList();

        // Resolved per job (not captured at startup): the typed client's handler rotation and the
        // test factory's fake-sender override both want scope-lifetime resolution.
        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var deadTokens = await sender.SendAsync(messages, cancellationToken);

        if (deadTokens.Count > 0)
        {
            var pruned = await db.DeviceTokens
                .Where(t => deadTokens.Contains(t.Token))
                .ExecuteDeleteAsync(cancellationToken);
            _logger.LogInformation(
                "Pruned {PrunedCount} dead device tokens after pushing {NotificationType}",
                pruned, job.Type);
        }

        _logger.LogDebug(
            "Pushed {NotificationType} to {DeviceCount} devices across {RecipientCount} recipients in {EnvironmentId}",
            job.Type, messages.Count, job.Recipients.Count, job.EnvironmentId);
    }
}
