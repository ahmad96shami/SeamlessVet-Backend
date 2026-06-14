using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VetSystem.Application.Common;

namespace VetSystem.API.Identity;

/// <summary>
/// M35 — HS256 platform-admin access tokens, signed by the same <see cref="JwtOptions.SecretKey"/> as
/// tenant tokens (so the single JwtBearer validator accepts both). The token carries
/// <c>platform_admin=true</c> and NO <c>environment_id</c>/<c>role</c>, so it is invisible to the
/// env-scoped query filter and is gated to <c>/platform/*</c> by <c>RequirePlatformAdminFilter</c>.
/// No refresh in v1 — the longer <see cref="JwtOptions.PlatformTokenMinutes"/> lifetime is re-minted by login.
/// </summary>
public sealed class PlatformTokenService : IPlatformTokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SymmetricSecurityKey _signingKey;

    public PlatformTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_options.SecretKey)
            || _options.SecretKey.StartsWith("PLACEHOLDER", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Override via dotnet user-secrets (UserSecretsId: vet-system-secrets).");
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
    }

    public AccessTokenResult IssuePlatformToken(PlatformPrincipal principal)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.PlatformTokenMinutes);

        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, principal.PlatformAdminId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                new Claim(HttpCurrentUserAccessor.PlatformAdminClaim, "true"),
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new AccessTokenResult(handler.WriteToken(token), expires);
    }
}
