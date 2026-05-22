using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Identity;

public sealed class EfRefreshTokenStore : IRefreshTokenStore
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public EfRefreshTokenStore(ApplicationDbContext db, IPasswordHasher hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<StoredRefreshToken> IssueAsync(
        Guid userId,
        Guid environmentId,
        string rawToken,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var entity = new RefreshToken
        {
            UserId = userId,
            EnvironmentId = environmentId,
            TokenHash = _hasher.Hash(rawToken),
            ExpiresAt = _clock.UtcNow.Add(lifetime),
        };

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return ToStored(entity);
    }

    public async Task<StoredRefreshToken?> FindActiveAsync(
        string rawToken,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        // Cannot WHERE on hash because BCrypt is salt-randomised — scan candidates in the env
        // and verify each. In practice this set is small (one user's outstanding refreshes
        // typically ≤ 5) because we revoke on rotation.
        var now = _clock.UtcNow;
        var candidates = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.EnvironmentId == environmentId
                        && t.RevokedAt == null
                        && t.ExpiresAt > now
                        && t.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var match = candidates.FirstOrDefault(t => _hasher.Verify(rawToken, t.TokenHash));
        return match is null ? null : ToStored(match);
    }

    public async Task<StoredRefreshToken> RotateAsync(
        StoredRefreshToken current,
        string newRawToken,
        TimeSpan newLifetime,
        CancellationToken cancellationToken)
    {
        var existing = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == current.Id, cancellationToken);

        var now = _clock.UtcNow;
        var replacement = new RefreshToken
        {
            UserId = existing.UserId,
            EnvironmentId = existing.EnvironmentId,
            TokenHash = _hasher.Hash(newRawToken),
            ExpiresAt = now.Add(newLifetime),
        };

        _db.RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync(cancellationToken);

        existing.RevokedAt = now;
        existing.RevocationReason = "rotated";
        existing.ReplacedById = replacement.Id;
        await _db.SaveChangesAsync(cancellationToken);

        return ToStored(replacement);
    }

    public async Task RevokeAsync(StoredRefreshToken current, string reason, CancellationToken cancellationToken)
    {
        var existing = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == current.Id, cancellationToken);

        if (existing is null || existing.RevokedAt is not null)
        {
            return;
        }

        existing.RevokedAt = _clock.UtcNow;
        existing.RevocationReason = reason;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static StoredRefreshToken ToStored(RefreshToken t)
        => new(t.Id, t.UserId, t.EnvironmentId, t.ExpiresAt, t.RevokedAt, t.ReplacedById);
}
