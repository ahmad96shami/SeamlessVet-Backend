namespace VetSystem.Application.Notifications;

/// <summary>
/// M21 — the out-of-process push channel (Expo in Infrastructure). Best-effort by contract:
/// implementations swallow transport failures (a push is a courtesy, never a business
/// dependency) and report only the tokens the provider declared dead so the caller can prune
/// them. One message per device token; the caller fans out per recipient.
/// </summary>
public interface IPushSender
{
    /// <returns>The tokens the provider rejected as no-longer-registered (prune candidates).</returns>
    Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<PushMessage> messages,
        CancellationToken cancellationToken);
}

/// <summary>
/// One push to one device. <paramref name="Data"/> is the deeplink payload and MUST mirror the
/// SignalR realtime shape (<c>{ notificationId, type, payload }</c>) — the mobile router consumes
/// both channels through the same code path.
/// </summary>
public sealed record PushMessage(
    string Token,
    string? Title,
    string? Body,
    IReadOnlyDictionary<string, object?> Data);
