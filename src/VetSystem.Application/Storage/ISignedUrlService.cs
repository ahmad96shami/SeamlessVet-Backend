namespace VetSystem.Application.Storage;

/// <summary>
/// Mints short-lived signed URLs for the private object store (Cloudflare R2 / S3-compatible).
/// The bucket is never public: clients upload directly via a signed PUT and read via a signed GET
/// (default 5-minute TTL — CLAUDE.md storage rule). DB columns hold only the opaque object key,
/// never a URL.
/// </summary>
public interface ISignedUrlService
{
    /// <summary>Signed PUT URL the client uploads the object to directly.</summary>
    Task<SignedUrl> MintPutAsync(string objectKey, string? contentType = null, TimeSpan? ttl = null);

    /// <summary>Signed GET URL for reads (M5 task 16). Used by any endpoint returning attachment data.</summary>
    Task<SignedUrl> MintGetAsync(string objectKey, TimeSpan? ttl = null);
}

public sealed record SignedUrl(string Url, DateTimeOffset ExpiresAt);
