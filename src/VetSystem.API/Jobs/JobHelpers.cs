using System.Text.Json;
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

    /// <summary>
    /// Reads a GUID <paramref name="property"/> out of a notification's serialized JSON payload — the
    /// dedupe key for the once-per-day reminder scans. A null/malformed payload never breaks the scan.
    /// </summary>
    public static Guid? TryReadPayloadGuid(string? json, string property)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var element) && element.TryGetGuid(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // A malformed payload should never break the scan.
        }

        return null;
    }
}
