using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Catalog;

/// <summary>
/// M2 task 11 — confirms the read-only handshake that puts products + services on the field
/// device without a client-side write endpoint:
///
/// 1. <c>powersync/sync-rules.yaml</c> exposes a <c>reference</c> bucket selecting
///    <c>products</c> and <c>services</c> (so PowerSync's logical-replication stream forwards
///    them to every connected doctor).
/// 2. Rows admins create via <c>/admin/products</c> and <c>/admin/services</c> are persisted in
///    the same Postgres tables that bucket reads from — i.e. the read path needs nothing more
///    than the admin write path already provides.
/// 3. The <c>/sync/{table}</c> dispatcher has no handler for those tables, so a client cannot
///    write to them (pull-only is enforced server-side, not just convention).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReferenceBucketSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_ExposesProductsAndServicesInReferenceBucket()
    {
        var rulesPath = LocateSyncRulesFile();
        var contents = File.ReadAllText(rulesPath);

        contents.Should().Contain("reference:", "the M2 reference bucket must be declared");
        contents.Should().MatchRegex(@"SELECT\s+\*\s+FROM\s+products",
            "reference bucket must pull the products catalog");
        contents.Should().MatchRegex(@"SELECT\s+\*\s+FROM\s+services",
            "reference bucket must pull the services catalog");
    }

    /// <summary>
    /// M36 — the reference stream has no doctor filter, so before this milestone a doctor in center A
    /// streamed center B's products/services. The env predicate is the only thing scoping it: both
    /// the products and the services query must compare <c>environment_id</c> to the trusted token
    /// parameter, so a pre-M36 token (no env claim) fails closed and never leaks another tenant.
    /// </summary>
    [Fact]
    public void SyncRulesYaml_ScopesReferenceDataByEnvironment()
    {
        var contents = File.ReadAllText(LocateSyncRulesFile());

        contents.Should().MatchRegex(
            @"SELECT\s+\*\s+FROM\s+products\s+WHERE\s+environment_id\s*=\s*auth\.parameters\(\)\s*->>\s*'environment_id'",
            "the reference stream's products query must be scoped to the token's environment (M36)");
        contents.Should().MatchRegex(
            @"SELECT\s+\*\s+FROM\s+services\s+WHERE\s+environment_id\s*=\s*auth\.parameters\(\)\s*->>\s*'environment_id'",
            "the reference stream's services query must be scoped to the token's environment (M36)");

        // Defense-in-depth: the shared CTE anchors that drive every doctor-scoped stream are env-scoped too.
        contents.Should().MatchRegex(
            @"my_customers:\s*SELECT\s+id\s+AS\s+customer_id\s+FROM\s+customers\s+WHERE\s+assigned_doctor_id\s*=\s*auth\.user_id\(\)\s+AND\s+environment_id\s*=\s*auth\.parameters\(\)\s*->>\s*'environment_id'",
            "the my_customers CTE anchor must carry the env predicate (M36 defense-in-depth)");
    }

    [Fact]
    public async Task AdminWrites_PersistRowsThatTheReferenceBucketWouldSync()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var productId = await CreateProductAsync(client);
        var serviceId = await CreateServiceAsync(client);

        // The reference bucket is `SELECT * FROM products / services WHERE deleted_at IS NULL`.
        // Verify those rows exist in DB exactly as PowerSync would observe them.
        await using var verifyDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var product = await verifyDb.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId);
        product.Should().NotBeNull("admin POST /admin/products must persist a syncable row");
        product!.DeletedAt.Should().BeNull("syncable rows must be live, not tombstoned");

        var service = await verifyDb.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId);
        service.Should().NotBeNull("admin POST /admin/services must persist a syncable row");
        service!.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task ClientSyncWrites_AreRejectedForProductsAndServices()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        await ExpectSyncRejected(client, "products");
        await ExpectSyncRejected(client, "services");
    }

    private static async Task<Guid> CreateProductAsync(HttpClient client)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/products")
        {
            Content = JsonContent.Create(new
            {
                NameAr = "أموكسيسيلين",
                NameLatin = "Amoxicillin",
                Category = ProductCategory.Medication,
                PurchasePrice = 12.50m,
                SellingPrice = 25.00m,
                ReorderPoint = 10m,
            }),
        };
        req.Headers.Add("Idempotency-Key", $"product-create-{Guid.NewGuid():N}"[..32]);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateServiceAsync(HttpClient client)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/services")
        {
            Content = JsonContent.Create(new
            {
                NameAr = "كشفية",
                NameLatin = "General Examination",
                Category = "exam",
                DefaultPrice = 50.00m,
            }),
        };
        req.Headers.Add("Idempotency-Key", $"service-create-{Guid.NewGuid():N}"[..32]);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task ExpectSyncRejected(HttpClient client, string table)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/sync/{table}")
        {
            Content = JsonContent.Create(new { id = Guid.CreateVersion7(), name_ar = "client-write" }),
        };
        req.Headers.Add("Idempotency-Key", $"reject-{table}-{Guid.NewGuid():N}"[..32]);

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().NotBe(HttpStatusCode.OK,
            $"/sync/{table} must not have a write handler — reference data is pull-only.");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.MethodNotAllowed);
    }

    private static string LocateSyncRulesFile()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "powersync", "sync-rules.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("powersync/sync-rules.yaml not found above the test binary.");
    }
}
