using VetSystem.Domain.Common;

namespace VetSystem.Application.Common;

/// <summary>
/// In-process publish seam for <see cref="IDomainEvent"/>s. M4 publishes
/// <c>NegativeStockAttemptedEvent</c>; the default implementation just logs (the event is
/// "recorded" for the M4 exit criteria). M11 replaces/extends the implementation to dispatch to
/// the SignalR hub + persist <c>notifications</c> rows. Kept in Application so services depend on
/// the abstraction, never on the transport.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
