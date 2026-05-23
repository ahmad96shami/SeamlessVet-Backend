using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// Shared bits for the M11 Hangfire jobs. Jobs run without an HTTP principal, so they enumerate every
/// environment explicitly (bypassing the env query filter) and read "today" from <see cref="IClock"/>
/// so a forced clock drives them deterministically in tests.
/// </summary>
internal static class JobHelpers
{
    /// <summary>UTC midnight of the clock's current day — the dedupe window for once-per-day jobs.</summary>
    public static DateTimeOffset StartOfTodayUtc(IClock clock)
        => new(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public static Task<List<Guid>> ActiveEnvironmentIdsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        => db.Environments
            .IgnoreQueryFilters()
            .Where(e => e.DeletedAt == null)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
}
