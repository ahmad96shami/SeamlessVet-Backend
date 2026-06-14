namespace VetSystem.API.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// M35 — lifetime of a platform-admin access token. Longer than a tenant access token because the
    /// platform console has no refresh flow in v1 (rare manual ops); the token is re-issued by login.
    /// </summary>
    public int PlatformTokenMinutes { get; set; } = 480;
}

public sealed class PowerSyncOptions
{
    public const string SectionName = "PowerSync";

    public string Issuer { get; set; } = "vet-system-api";
    public string Audience { get; set; } = "powersync";
    public int TokenLifetimeMinutes { get; set; } = 60;
    public PowerSyncSigningKeyOptions SigningKey { get; set; } = new();
}

public sealed class PowerSyncSigningKeyOptions
{
    public string Kid { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
}
