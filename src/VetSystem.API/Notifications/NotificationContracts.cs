using System.Text.Json;

namespace VetSystem.API.Notifications;

/// <summary>
/// SignalR group conventions (TECH_STACK "SignalR Hub"). The admin and environment groups are
/// <b>environment-scoped</b> — never a global <c>admins</c> group — so a push can never cross an
/// environment boundary (SCHEMA invariant #6). Per-user delivery (<see cref="User"/>) is what the
/// dispatcher actually targets; the broader groups are joined on connect for future broadcast use.
/// </summary>
public static class NotificationGroups
{
    public static string User(Guid userId) => $"user_{userId}";

    public static string User(string userId) => $"user_{userId}";

    public static string Environment(string environmentId) => $"environment_{environmentId}";

    public static string Admins(string environmentId) => $"admins_{environmentId}";
}

/// <summary>Strongly-typed hub client surface — the one method clients subscribe to.</summary>
public interface INotificationClient
{
    Task ReceiveNotification(NotificationPayload notification);
}

/// <summary>The realtime push shape (also the per-item shape returned by the feed endpoint).</summary>
public sealed record NotificationPayload(
    Guid Id,
    string Type,
    string? Title,
    string? Body,
    JsonElement? Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);
