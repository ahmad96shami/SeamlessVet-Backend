using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VetSystem.Application.Common;

namespace VetSystem.API.Identity;

/// <summary>
/// Loads the PowerSync signing key from configuration (PEM RSA, override via dotnet user-secrets)
/// and falls back to an in-process ephemeral key for local dev. Production: configure a stable
/// PEM in user-secrets so the JWKS exposes the same kid across restarts.
/// </summary>
public sealed class PowerSyncTokenService : IPowerSyncTokenService, IDisposable
{
    private readonly PowerSyncOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<PowerSyncTokenService> _logger;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly string _kid;
    private readonly JsonWebKey _publicJwk;

    public PowerSyncTokenService(
        IOptions<PowerSyncOptions> options,
        IClock clock,
        ILogger<PowerSyncTokenService> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;

        (_rsa, _kid) = LoadOrCreateRsa(_options.SigningKey, _logger);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = _kid };
        _publicJwk = BuildPublicJwk(_rsa, _kid);
    }

    public PowerSyncTokenResult IssueToken(Guid userId)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.TokenLifetimeMinutes);

        var handler = new JwtSecurityTokenHandler();
        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("user_id", userId.ToString()),
                // iat as a NumericDate (JSON number). The PowerSync Service's `jose` verifier
                // requires this claim; the JwtSecurityToken ctor only sets nbf/exp, so add it
                // explicitly (Integer64 → serialized as a number, not a string).
                new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        jwt.Header["kid"] = _kid;

        return new PowerSyncTokenResult(handler.WriteToken(jwt), expires);
    }

    public IReadOnlyList<JsonWebKey> GetJwks() => [_publicJwk];

    public void Dispose() => _rsa.Dispose();

    private static (RSA rsa, string kid) LoadOrCreateRsa(
        PowerSyncSigningKeyOptions opts,
        ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(opts.PrivateKeyPem)
            && !opts.PrivateKeyPem.StartsWith("PLACEHOLDER", StringComparison.Ordinal))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(opts.PrivateKeyPem);
            var kid = string.IsNullOrWhiteSpace(opts.Kid) || opts.Kid.StartsWith("PLACEHOLDER")
                ? DeriveKid(rsa)
                : opts.Kid;
            return (rsa, kid);
        }

        logger.LogWarning(
            "PowerSync signing key not configured; generating an ephemeral RSA key for this process. "
            + "Set PowerSync:SigningKey:PrivateKeyPem via dotnet user-secrets for stable JWKS.");

        var ephemeral = RSA.Create(2048);
        return (ephemeral, DeriveKid(ephemeral));
    }

    private static string DeriveKid(RSA rsa)
    {
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var thumb = SHA256.HashData(publicKey);
        return Base64UrlEncoder.Encode(thumb)[..16];
    }

    private static JsonWebKey BuildPublicJwk(RSA rsa, string kid)
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = SecurityAlgorithms.RsaSha256,
            Kid = kid,
            N = Base64UrlEncoder.Encode(parameters.Modulus),
            E = Base64UrlEncoder.Encode(parameters.Exponent),
        };
    }
}
