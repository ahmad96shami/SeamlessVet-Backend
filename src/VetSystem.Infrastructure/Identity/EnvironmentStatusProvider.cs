using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VetSystem.Application.Identity;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Identity;

/// <summary>
/// EF-backed environment-status resolver with a short in-process cache (mirrors
/// <see cref="PermissionResolver"/>). Reads with <c>IgnoreQueryFilters()</c> because the caller's
/// own environment filter would otherwise hide a soft-deleted env (and the status column is not in
/// the filter), so a missing/deleted env must be detectable to return <c>environment_suspended</c>.
/// </summary>
public sealed class EnvironmentStatusProvider : IEnvironmentStatusProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(45);
    private const string CacheKeyPrefix = "envstatus:";

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public EnvironmentStatusProvider(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> GetStatusAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var key = CacheKey(environmentId);
        if (_cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        // Project to a sentinel so a genuine null (not-found/deleted) is cacheable and distinct from
        // a cache miss. EF can't translate the ternary, so fetch the row then evaluate in memory.
        var row = await _db.Environments
            .IgnoreQueryFilters()
            .Where(e => e.Id == environmentId)
            .Select(e => new { e.Status, e.DeletedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var status = row is null || row.DeletedAt is not null ? null : row.Status;
        _cache.Set(key, status, CacheLifetime);
        return status;
    }

    public void Invalidate(Guid environmentId) => _cache.Remove(CacheKey(environmentId));

    private static string CacheKey(Guid environmentId) => CacheKeyPrefix + environmentId;
}
