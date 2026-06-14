namespace VetSystem.Application.Common;

/// <summary>
/// M35 — mints the platform super-admin access token returned from <c>/platform/auth/login</c>.
/// Signed with the same HS256 key/issuer/audience as a tenant token (so one JwtBearer validator
/// covers both), but carries <c>platform_admin=true</c> and — deliberately — <b>no</b>
/// <c>environment_id</c> or <c>role</c> claim. No refresh flow in v1; the token is simply re-issued
/// by logging in again.
/// </summary>
public interface IPlatformTokenService
{
    AccessTokenResult IssuePlatformToken(PlatformPrincipal principal);
}

public sealed record PlatformPrincipal(Guid PlatformAdminId);
