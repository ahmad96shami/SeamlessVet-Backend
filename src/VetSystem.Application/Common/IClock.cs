namespace VetSystem.Application.Common;

/// <summary>
/// Testable clock abstraction. Hangfire jobs (M11 vaccination reminders, etc.) and timestamp
/// generation in the DbContext interceptor consume this so tests can drive a forced time.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
