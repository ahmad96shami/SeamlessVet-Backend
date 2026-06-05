using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Notifications;

/// <summary>
/// API-layer <see cref="INotificationDispatcher"/> — the only place a <c>notifications</c> row and a
/// SignalR push are produced, so the persisted feed and realtime stream stay in lockstep (M11 task 7).
/// Rows are stamped with the dispatch's environment explicitly (callers resolve recipients per env),
/// which the audit interceptor requires and which keeps delivery environment-isolated even when the
/// caller is a background worker with no HTTP principal.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<NotificationsHub, INotificationClient> _hub;
    private readonly PushQueue _pushQueue;

    public NotificationDispatcher(
        ApplicationDbContext db,
        IHubContext<NotificationsHub, INotificationClient> hub,
        PushQueue pushQueue)
    {
        _db = db;
        _hub = hub;
        _pushQueue = pushQueue;
    }

    public async Task DispatchAsync(NotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        var recipients = dispatch.Recipients.Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        JsonElement? payloadElement = dispatch.Payload is null ? null : JsonSerializer.SerializeToElement(dispatch.Payload);
        var payloadJson = payloadElement?.GetRawText();

        var rows = recipients.Select(userId => new Notification
        {
            EnvironmentId = dispatch.EnvironmentId,
            UserId = userId,
            Type = dispatch.Type,
            Title = dispatch.Title,
            Body = dispatch.Body,
            Payload = payloadJson,
        }).ToList();

        _db.Notifications.AddRange(rows);
        await _db.SaveChangesAsync(cancellationToken);

        // Push after the commit so each realtime event mirrors a row the feed endpoint will return.
        foreach (var row in rows)
        {
            var push = new NotificationPayload(row.Id, row.Type, row.Title, row.Body, payloadElement, row.CreatedAt, row.ReadAt);
            await _hub.Clients.Group(NotificationGroups.User(row.UserId)).ReceiveNotification(push);
        }

        // M21 — remote push rides the same commit, but off-thread: callers (domain-event handlers on
        // hot business paths) must never wait on an external HTTPS call. Per-recipient row ids travel
        // along so each device's deeplink lands on its owner's feed row.
        _pushQueue.Enqueue(new PushJob(
            dispatch.EnvironmentId,
            rows.Select(r => new PushRecipient(r.UserId, r.Id)).ToList(),
            dispatch.Type,
            dispatch.Title,
            dispatch.Body,
            payloadJson));
    }
}
