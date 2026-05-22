using VetSystem.Application.Common;

namespace VetSystem.Tests.Infrastructure;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class FakeCurrentUser : ICurrentUserAccessor
{
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string? Role { get; set; }
    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();
}
