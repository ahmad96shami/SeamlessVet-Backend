using System.Threading.Channels;

namespace VetSystem.API.Notifications;

/// <summary>One recipient of a dispatched notification — each got their own feed row.</summary>
public sealed record PushRecipient(Guid UserId, Guid NotificationId);

/// <summary>
/// A remote-push work item mirroring one <c>NotificationDispatch</c>: the worker resolves each
/// recipient's device tokens and sends, stamping the recipient's own <c>NotificationId</c> into the
/// deeplink data (rows are per-recipient, so a single shared id would mis-attribute them).
/// </summary>
public sealed record PushJob(
    Guid EnvironmentId,
    IReadOnlyList<PushRecipient> Recipients,
    string Type,
    string? Title,
    string? Body,
    string? PayloadJson);

/// <summary>
/// M21 — the in-process hand-off between <see cref="NotificationDispatcher"/> (hot business paths;
/// domain-event handlers are awaited inline) and <see cref="PushDispatchHostedService"/> (the one
/// place an external HTTPS call is allowed to take its time). Deliberately a memory channel, not
/// Hangfire: it works identically in every environment (Hangfire is off under Test) and push is
/// best-effort by contract — losing queued pushes on a process crash mirrors the SignalR channel,
/// which has no durability either.
/// </summary>
public sealed class PushQueue
{
    private readonly Channel<PushJob> _channel =
        Channel.CreateUnbounded<PushJob>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<PushJob> Reader => _channel.Reader;

    /// <summary>Never blocks (unbounded); call only after the notification rows are committed.</summary>
    public void Enqueue(PushJob job) => _channel.Writer.TryWrite(job);
}
