using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using VetSystem.Application.Storage;

namespace VetSystem.Infrastructure.Storage;

/// <summary>
/// <see cref="ISignedUrlService"/> over Cloudflare R2 via the AWS S3 SDK (R2 is S3-compatible).
/// Presigning is a local cryptographic operation — no network round-trip — so the singleton client
/// is cheap and thread-safe. Path-style addressing + the <c>auto</c> region are required for R2.
/// </summary>
public sealed class R2SignedUrlService : ISignedUrlService, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly R2Options _options;

    public R2SignedUrlService(IOptions<R2Options> options)
    {
        _options = options.Value;

        var config = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = _options.Region,
        };

        _s3 = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), config);
    }

    public Task<SignedUrl> MintPutAsync(string objectKey, string? contentType = null, TimeSpan? ttl = null)
        => SignAsync(objectKey, HttpVerb.PUT, contentType, ttl);

    public Task<SignedUrl> MintGetAsync(string objectKey, TimeSpan? ttl = null)
        => SignAsync(objectKey, HttpVerb.GET, contentType: null, ttl);

    private async Task<SignedUrl> SignAsync(string objectKey, HttpVerb verb, string? contentType, TimeSpan? ttl)
    {
        var expiresAt = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(_options.SignedUrlTtlMinutes));

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = verb,
            Expires = expiresAt,
        };

        if (contentType is not null)
        {
            request.ContentType = contentType;
        }

        var url = await _s3.GetPreSignedURLAsync(request);
        return new SignedUrl(url, new DateTimeOffset(expiresAt));
    }

    public void Dispose() => _s3.Dispose();
}
