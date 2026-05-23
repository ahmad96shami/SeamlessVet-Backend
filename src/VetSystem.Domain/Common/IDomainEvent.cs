namespace VetSystem.Domain.Common;

/// <summary>
/// Marker for an in-process domain event. Raised by the Application/Infrastructure layers and
/// fanned out by <c>IDomainEventPublisher</c>. M4 raises <c>NegativeStockAttemptedEvent</c>;
/// M11 adds the SignalR / notification subscribers that consume these.
/// </summary>
public interface IDomainEvent
{
}
