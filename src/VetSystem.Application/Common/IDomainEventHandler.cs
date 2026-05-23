using VetSystem.Domain.Common;

namespace VetSystem.Application.Common;

/// <summary>
/// Handles one kind of <see cref="IDomainEvent"/>. Resolved and invoked by
/// <c>IDomainEventPublisher</c> in a fresh DI scope per publish, so a handler's work (e.g. persisting
/// a <c>notifications</c> row) is independent of the publisher's caller — this is what lets the
/// negative-stock notification survive even though the offending inventory write is rolled back.
/// A handler throwing is logged and swallowed by the publisher: a notification failure must never
/// break the business operation that raised the event.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
