using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.Infrastructure.Messaging;

/// <summary>
/// In-process <see cref="IDomainEventPublisher"/> that fans an event out to every registered
/// <see cref="IDomainEventHandler{TEvent}"/> for its runtime type. Each publish runs handlers in a
/// <b>fresh DI scope</b> (its own <c>DbContext</c>), deliberately decoupled from the caller's unit of
/// work: this is what lets the negative-stock notification persist even though the inventory write
/// that raised the event is rolled back. Handlers are best-effort — a thrown handler is logged and
/// swallowed so a notification failure never breaks the business operation. Singleton-safe (resolves
/// scopes on demand via <see cref="IServiceScopeFactory"/>).
/// </summary>
public sealed class DispatchingDomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DispatchingDomainEventPublisher> _logger;

    public DispatchingDomainEventPublisher(
        IServiceScopeFactory scopeFactory,
        ILogger<DispatchingDomainEventPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();

        if (handlers.Count == 0)
        {
            _logger.LogDebug("Domain event {EventType} published with no registered handler.", eventType.Name);
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                // Late-bound dispatch to IDomainEventHandler<TConcrete>.HandleAsync(TConcrete, ct).
                await ((dynamic)handler!).HandleAsync((dynamic)domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Domain event handler {Handler} failed for {EventType}; swallowed so the source operation is unaffected.",
                    handler!.GetType().Name,
                    eventType.Name);
            }
        }
    }
}
