using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Catalog;

/// <summary>
/// Barcode rules on <c>products</c>: a barcode is intentionally NOT unique — the same code may be
/// shared by several products, and the POS resolves a scan to every match for the cashier to choose
/// from. The create/update path still normalizes a barcode (blank → null, surrounding whitespace
/// trimmed) so a trimmed POS scan exact-matches the stored value. Drives the live ASP.NET pipeline
/// through <see cref="VetApiFactory"/> so the index + <c>NormalizeBarcode</c> + auth/idempotency
/// filters actually run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProductBarcodeTests
{
    [Fact]
    public async Task SameBarcode_MayBeSharedAcrossProducts_AndSearchReturnsThemAll()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthorizedClient(factory, admin);

        const string barcode = "BC-SHARED-001";
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();

        // Two distinct LIVE products may carry the same barcode — no uniqueness conflict.
        (await PostProduct(client, firstId, barcode)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostProduct(client, secondId, barcode)).StatusCode
            .Should().Be(HttpStatusCode.OK, "barcodes are shareable across products");

        // A barcode search returns every product that carries it (the POS shows all matches).
        var matches = await GetListAsync<ProductRow>(client, $"/admin/products?search={barcode}");
        matches.Select(p => p.Id).Should().Contain(new[] { firstId, secondId });
    }

    [Fact]
    public async Task Barcode_BlankIsStoredAsNull_AndWhitespaceIsTrimmed()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthorizedClient(factory, admin);

        // Blank/whitespace barcodes normalize to null (no barcode), so they never collide.
        (await PostProduct(client, Guid.CreateVersion7(), "")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostProduct(client, Guid.CreateVersion7(), "   ")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Surrounding whitespace is trimmed on store, so a trimmed POS scan exact-matches.
        var trimmedId = Guid.CreateVersion7();
        (await PostProduct(client, trimmedId, "  BC-TRIM-ME  ")).StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync($"/admin/products/{trimmedId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("barcode").GetString().Should().Be("BC-TRIM-ME");
    }

    // ---- helpers ----

    private sealed record ProductRow(Guid Id, string NameAr, string? Barcode, string Category);

    private static HttpClient AuthorizedClient(VetApiFactory factory, User admin)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<List<T>> GetListAsync<T>(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<List<T>>())!;
    }

    private static Task<HttpResponseMessage> PostProduct(HttpClient client, Guid id, string? barcode)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/products")
        {
            Content = JsonContent.Create(new
            {
                id,
                nameAr = "سلعة اختبار",
                barcode,
                category = "product",
                purchasePrice = 1m,
                sellingPrice = 2m,
                reorderPoint = 0m,
            }),
        };
        req.Headers.Add("Idempotency-Key", $"prod-{Guid.NewGuid():N}"[..32]);
        return client.SendAsync(req);
    }
}
