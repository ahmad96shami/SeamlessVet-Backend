namespace VetSystem.Application.Notifications;

/// <summary>
/// The single seam every notification source uses (domain-event handlers + Hangfire jobs). One call
/// persists a <c>notifications</c> row per recipient <b>and</b> pushes it over SignalR to that user's
/// group, so the realtime push and the <c>GET /notifications</c> feed never disagree (M11 task 7).
/// Recipients are explicit user ids resolved by the caller within the target environment, which keeps
/// delivery environment-isolated (SCHEMA invariant #6). Implemented in the API layer where the hub lives.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationDispatch dispatch, CancellationToken cancellationToken);
}

/// <summary>
/// One notification to fan out to a set of recipients in a single environment. <see cref="Payload"/>
/// is an arbitrary object serialized to the row's <c>jsonb</c> column and pushed as-is to clients,
/// which localize/link from <see cref="Type"/> + the payload rather than the human-readable text.
/// </summary>
public sealed record NotificationDispatch(
    Guid EnvironmentId,
    IReadOnlyCollection<Guid> Recipients,
    string Type,
    string? Title,
    string? Body,
    object? Payload);
