namespace VetSystem.Application.Common;

/// <summary>
/// Mints the user access token + refresh-token pair returned from <c>/auth/login</c> and
/// <c>/auth/refresh</c>. Claims on the access token: <c>sub</c>, <c>role</c>, <c>environment_id</c>
/// (per <c>vet-backend/CLAUDE.md</c>). Refresh tokens are opaque random strings; the server-side
/// hash + rotation chain lives in <see cref="IRefreshTokenStore"/>.
/// </summary>
public interface IJwtTokenService
{
    AccessTokenResult IssueAccessToken(UserPrincipal principal);

    string IssueRefreshTokenValue();
}

public sealed record UserPrincipal(
    Guid UserId,
    Guid EnvironmentId,
    string RoleKey);

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
