using Microsoft.Extensions.Logging;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.Infrastructure.Messaging;

/// <summary>
/// Default <see cref="IDomainEventPublisher"/> — logs each event (so it is "recorded" per the M4
/// exit criteria) and returns. There is no subscriber wiring yet; M11 replaces this with the
/// SignalR fan-out + <c>notifications</c> persistence. Singleton-safe (stateless beyond the logger).
/// </summary>
public sealed class InMemoryDomainEventPublisher : IDomainEventPublisher
{
    private readonly ILogger<InMemoryDomainEventPublisher> _logger;

    public InMemoryDomainEventPublisher(ILogger<InMemoryDomainEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Domain event {EventType} published: {@DomainEvent}",
            domainEvent.GetType().Name,
            domainEvent);

        return Task.CompletedTask;
    }
}
