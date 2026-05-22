using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// Server-side refresh-token persistence backing M1's <c>POST /auth/refresh</c>. Token rotation
/// chains every refresh through <see cref="ReplacedById"/> so a leaked token can be traced. The
/// raw token never lives at rest — only <see cref="TokenHash"/> (BCrypt) does.
/// </summary>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevocationReason { get; set; }

    public Guid? ReplacedById { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
