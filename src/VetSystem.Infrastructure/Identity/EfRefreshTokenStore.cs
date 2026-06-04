using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Identity;

public sealed class EfRefreshTokenStore : IRefreshTokenStore
{
    private readonly ApplicationDbContext _db;
    private readonly IRefreshTokenHasher _hasher;
    private readonly IClock _clock;

    public EfRefreshTokenStore(ApplicationDbContext db, IRefreshTokenHasher hasher, IClock clock)
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
        // Deterministic SHA-256 → exact match via ix_refresh_tokens_hash. The previous BCrypt
        // scan verified every active row in the env (~300ms each), so a single failed refresh
        // hung for minutes once a few hundred logins accumulated.
        var now = _clock.UtcNow;
        var hash = _hasher.Hash(rawToken);
        var match = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.TokenHash == hash
                     && t.EnvironmentId == environmentId
                     && t.RevokedAt == null
                     && t.ExpiresAt > now
                     && t.DeletedAt == null,
                cancellationToken);

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
