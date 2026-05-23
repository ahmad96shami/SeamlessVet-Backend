namespace VetSystem.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 connection settings (bound from the <c>R2</c> config section). Real values come
/// from <c>dotnet user-secrets</c> / env; <c>appsettings.json</c> ships placeholders only.
/// </summary>
public sealed class R2Options
{
    public const string SectionName = "R2";

    public string ServiceUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    /// <summary>Default signed-URL lifetime in minutes (5 per CLAUDE.md).</summary>
    public int SignedUrlTtlMinutes { get; set; } = 5;

    /// <summary>R2 uses the virtual region <c>auto</c>; required for SigV4 signing.</summary>
    public string Region { get; set; } = "auto";
}
