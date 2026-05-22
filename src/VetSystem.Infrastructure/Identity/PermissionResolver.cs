using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Identity;

/// <summary>
/// EF-backed resolver. The cache is in-process (single-instance API; no Redis backplane per
/// TECH_STACK.md). M11's notification handlers and the user-admin endpoints in M1 call
/// <see cref="Invalidate"/> on any role/override mutation.
/// </summary>
public sealed class PermissionResolver : IPermissionResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "perms:";

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public PermissionResolver(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlySet<string>> ResolveAsync(
        Guid userId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var user = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.EnvironmentId == environmentId && u.DeletedAt == null)
            .Select(u => new { u.Id, u.RoleId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Cannot resolve permissions: user {userId} not found.");

        var rolePerms = await _db.RolePermissions
            .Where(rp => rp.RoleId == user.RoleId)
            .Join(_db.Permissions.IgnoreQueryFilters(),
                rp => rp.PermissionId,
                p => p.Id,
                (_, p) => p.Key)
            .ToListAsync(cancellationToken);

        var overrides = await _db.UserPermissionOverrides
            .IgnoreQueryFilters()
            .Where(o => o.UserId == userId && o.EnvironmentId == environmentId && o.DeletedAt == null)
            .Join(_db.Permissions.IgnoreQueryFilters(),
                o => o.PermissionId,
                p => p.Id,
                (o, p) => new { p.Key, o.Effect })
            .ToListAsync(cancellationToken);

        var effective = new HashSet<string>(rolePerms, StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
        {
            if (o.Effect == OverrideEffect.Grant) effective.Add(o.Key);
            else if (o.Effect == OverrideEffect.Deny) effective.Remove(o.Key);
        }

        var frozen = (IReadOnlySet<string>)effective;
        _cache.Set(key, frozen, CacheLifetime);
        return frozen;
    }

    public void Invalidate(Guid userId) => _cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => CacheKeyPrefix + userId;
}
