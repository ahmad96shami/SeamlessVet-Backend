using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.Tests.Infrastructure;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>
/// Captures every published <see cref="IDomainEvent"/> so tests can assert on them (e.g. M4's
/// negative-stock event). Registered as a singleton override of <see cref="IDomainEventPublisher"/>
/// in the in-process API host.
/// </summary>
internal sealed class CapturingDomainEventPublisher : IDomainEventPublisher
{
    private readonly List<IDomainEvent> _events = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<IDomainEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToList();
            }
        }
    }

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _events.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeCurrentUser : ICurrentUserAccessor
{
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string? Role { get; set; }
    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();
}
