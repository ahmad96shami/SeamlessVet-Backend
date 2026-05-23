using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using VetSystem.Application.Common;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Visits;

/// <summary>
/// M5 task 24 — full attachment round-trip against a real S3-compatible store (MinIO via
/// Testcontainers): request a presigned upload, PUT the bytes straight to the bucket, confirm,
/// then fetch the signed GET URL and verify it returns exactly those bytes. The DB only ever holds
/// the object key — never a public URL.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AttachmentR2RoundTripTests
{
    private const string Bucket = "vet-attachments-test";

    [Fact]
    public async Task PresignedUpload_PersistsKey_AndSignedGetReturnsTheObject()
    {
#pragma warning disable CS0618 // parameterless MinioBuilder ctor is obsolete; the module still pins a default image.
        await using var minio = new MinioBuilder().Build();
#pragma warning restore CS0618
        await minio.StartAsync();

        var serviceUrl = minio.GetConnectionString();
        var accessKey = minio.GetAccessKey();
        var secretKey = minio.GetSecretKey();
        await CreateBucketAsync(serviceUrl, accessKey, secretKey);

        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory
        {
            R2ServiceUrl = serviceUrl,
            R2AccessKey = accessKey,
            R2SecretKey = secretKey,
            R2Bucket = Bucket,
        };
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", jwt.IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Att Owner", phonePrimary = "+970590004321",
        })).EnsureSuccessStatusCode();

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress",
        })).EnsureSuccessStatusCode();

        // 1) Ask for a signed PUT URL.
        var attachmentId = Guid.CreateVersion7();
        var presign = await PostAsync(client, "/attachments/presigned-upload", new
        {
            id = attachmentId, visitId, fileType = "pdf", title = "lab",
        });
        presign.StatusCode.Should().Be(HttpStatusCode.OK);
        var presignBody = await presign.Content.ReadFromJsonAsync<JsonElement>();
        var uploadUrl = presignBody.GetProperty("uploadUrl").GetString()!;
        presignBody.GetProperty("attachmentId").GetGuid().Should().Be(attachmentId);
        uploadUrl.Should().StartWith(
            serviceUrl.TrimEnd('/'),
            $"presigned URL must target MinIO (serviceUrl={serviceUrl}, uploadUrl={uploadUrl})");

        // 2) Upload bytes directly to the bucket via the signed URL.
        var payload = Encoding.UTF8.GetBytes($"lab-report-{Guid.NewGuid()}");
        using var raw = new HttpClient();
        var put = await raw.PutAsync(uploadUrl, new ByteArrayContent(payload));
        put.StatusCode.Should().Be(HttpStatusCode.OK, "the presigned PUT must land in MinIO");

        // 3) Confirm the upload.
        var confirm = new HttpRequestMessage(HttpMethod.Patch, $"/attachments/{attachmentId}")
        {
            Content = JsonContent.Create(new { uploadStatus = "uploaded" }),
        };
        confirm.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        (await client.SendAsync(confirm)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 4) Read back: the GET returns a signed download URL that yields exactly the bytes.
        var get = await client.GetFromJsonAsync<JsonElement>($"/attachments/{attachmentId}");
        get.GetProperty("uploadStatus").GetString().Should().Be("uploaded");
        var downloadUrl = get.GetProperty("downloadUrl").GetString();
        downloadUrl.Should().NotBeNullOrEmpty();

        var downloaded = await raw.GetByteArrayAsync(downloadUrl);
        downloaded.Should().Equal(payload, "the signed GET URL returns the uploaded object");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task CreateBucketAsync(string serviceUrl, string accessKey, string secretKey)
    {
        using var s3 = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = Bucket });
    }
}
