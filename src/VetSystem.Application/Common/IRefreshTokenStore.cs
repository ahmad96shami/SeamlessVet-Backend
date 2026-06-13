namespace VetSystem.Application.Common;

/// <summary>
/// Server-side refresh-token persistence with rotation. Each refresh emits a new token and
/// links the old one through <c>replaced_by_id</c>; logout simply revokes the active token.
/// Implementation hashes the raw token with BCrypt before storing — the raw value never lives
/// at rest.
/// </summary>
public interface IRefreshTokenStore
{
    Task<StoredRefreshToken> IssueAsync(
        Guid userId,
        Guid environmentId,
        string rawToken,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds an active token by its raw value alone — the deterministic SHA-256 hash is globally
    /// unique, so the environment is read off the stored row (M34: refresh/logout no longer need the
    /// caller to know the env; the token self-identifies it).
    /// </summary>
    Task<StoredRefreshToken?> FindActiveByTokenAsync(
        string rawToken,
        CancellationToken cancellationToken);

    Task<StoredRefreshToken> RotateAsync(
        StoredRefreshToken current,
        string newRawToken,
        TimeSpan newLifetime,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        StoredRefreshToken current,
        string reason,
        CancellationToken cancellationToken);
}

public sealed record StoredRefreshToken(
    Guid Id,
    Guid UserId,
    Guid EnvironmentId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    Guid? ReplacedById);
