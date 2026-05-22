namespace VetSystem.API.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
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
