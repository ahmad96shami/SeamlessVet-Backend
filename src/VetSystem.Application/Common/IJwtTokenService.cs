namespace VetSystem.Application.Common;

/// <summary>
/// Mints the user access token + refresh-token pair returned from <c>/auth/login</c> and
/// <c>/auth/refresh</c>. Claims on the access token: <c>sub</c>, <c>role</c>, <c>environment_id</c>,
/// and one <c>perms</c> claim per effective permission (per <c>vet-backend/CLAUDE.md</c>) so
/// clients can gate UI by permission, not only by role. Refresh tokens are opaque random strings;
/// the server-side hash + rotation chain lives in <see cref="IRefreshTokenStore"/>.
/// </summary>
public interface IJwtTokenService
{
    AccessTokenResult IssueAccessToken(UserPrincipal principal);

    string IssueRefreshTokenValue();
}

/// <param name="Permissions">
/// The user's effective permission keys (role defaults ± per-user overrides, resolved via
/// <see cref="IPermissionResolver"/>). Emitted as <c>perms</c> claims. Defaults to none so
/// test/seed call sites that don't care about UI gating stay terse.
/// </param>
public sealed record UserPrincipal(
    Guid UserId,
    Guid EnvironmentId,
    string RoleKey,
    IReadOnlyCollection<string>? Permissions = null);

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
