using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VetSystem.Application.Common;

namespace VetSystem.API.Identity;

/// <summary>
/// HS256 access tokens signed by <see cref="JwtOptions.SecretKey"/>. Refresh tokens are
/// 256-bit random values returned in raw form to the client and stored as BCrypt hashes via
/// <see cref="IRefreshTokenStore"/>.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
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

    public AccessTokenResult IssueAccessToken(UserPrincipal principal)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, principal.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new("role", principal.RoleKey),
            new(ClaimTypes.Role, principal.RoleKey),
            new(HttpCurrentUserAccessor.EnvironmentIdClaim, principal.EnvironmentId.ToString()),
        };

        // One `perms` claim per effective permission so clients can gate UI by permission, not just
        // role (e.g. a receptionist granted invoices.write should see POS). A single permission
        // serialises as a scalar and multiple as a JSON array — clients normalise both.
        if (principal.Permissions is { Count: > 0 } perms)
        {
            claims.AddRange(perms.Select(p => new Claim(HttpCurrentUserAccessor.PermissionsClaim, p)));
        }

        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new AccessTokenResult(handler.WriteToken(token), expires);
    }

    public string IssueRefreshTokenValue()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncoder.Encode(buffer.ToArray());
    }
}
