using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Contracts;

/// <summary>
/// M8 tasks 15–17 + exit criteria, against a real Postgres env:
/// <list type="bullet">
/// <item>a field doctor's draft edits sync through <c>/sync/contracts</c> without server overwrite,
///       while an active contract is server-authoritative;</item>
/// <item>the <c>draft → active</c> activation gate returns 403 without <c>contracts.activate</c> and
///       activates + locks with it;</item>
/// <item>a field invoice bills catalog price whether the contract is draft or active — M29 removed
///       per-contract medication pricing, so a contract never overrides the catalog price.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class ContractsIntegrationTests
{
    [Fact]
    public async Task DraftEdit_ViaSync_Persists_WhileActiveIsServerAuthoritative()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        // Device authors a draft contract offline → uploads via /sync/contracts (snake_case payload).
        var contractId = Guid.CreateVersion7();
        (await SyncPutAsync(client, "contracts", new
        {
            id = contractId,
            customer_id = customerId,
            responsible_doctor_id = admin.Id,
            period_start = "2026-01-01",
            animal_type = "poultry",
            animal_count = 1000,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Device edits the draft's terms offline → uploads the change; the server accepts it (the
        // doctor-device is authoritative for a draft — no overwrite/rejection).
        (await SyncPatchAsync(client, "contracts", contractId, new { total_price = 5000m, animal_count = 1200 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var contract = await db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
            contract.Status.Should().Be(ContractStatus.Draft);
            contract.AnimalCount.Should().Be(1200, "the offline draft edit synced without server overwrite");
            contract.TotalPrice.Should().Be(5000m);
        }

        // Activate online → the contract becomes server-authoritative.
        (await PostAsync(client, $"/contracts/{contractId}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A late offline edit to the now-active contract is rejected; the server's terms stand.
        (await SyncPatchAsync(client, "contracts", contractId, new { total_price = 9999m }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Contracts.AsNoTracking().Where(c => c.Id == contractId).Select(c => c.TotalPrice).FirstAsync())
                .Should().Be(5000m, "an active contract is server-authoritative; the offline edit was not applied");
        }
    }

    [Fact]
    public async Task Activate_RequiresContractsActivatePermission()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var fieldVet = await SeedUserAsync(scope, RoleKey.VetField); // role has no role_permissions seeded
        await using var factory = new VetApiFactory();

        using var adminClient = AuthedClient(factory, admin);
        var customerId = await CreateCustomerAsync(adminClient);

        var contractId = Guid.CreateVersion7();
        (await PostAsync(adminClient, "/contracts", new
        {
            id = contractId,
            customerId,
            responsibleDoctorId = fieldVet.Id,
            periodStart = "2026-01-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // A user without contracts.activate cannot promote the draft.
        using var fieldClient = AuthedClient(factory, fieldVet, RoleKey.VetField);
        (await PostAsync(fieldClient, $"/contracts/{contractId}/activate", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Contracts.AsNoTracking().Where(c => c.Id == contractId).Select(c => c.Status).FirstAsync())
                .Should().Be(ContractStatus.Draft, "the gated activation wrote nothing");
        }

        // The admin holds contracts.activate → activation succeeds and stamps the audit fields.
        (await PostAsync(adminClient, $"/contracts/{contractId}/activate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var contract = await db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId);
            contract.Status.Should().Be(ContractStatus.Active);
            contract.ActivatedBy.Should().Be(admin.Id);
            contract.ActivatedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FieldInvoice_BillsCatalogPrice_WhetherContractDraftOrActive()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedProductAsync(scope, purchase: 10m, selling: 25m);
        await SeedFieldInventoryWithStockAsync(scope, admin.Id, productId, quantity: 100m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        // A contract for the customer (M29 removed per-medication price overrides, so the contract
        // carries no pricing terms — it never changes what a field visit bills).
        var contractId = Guid.CreateVersion7();
        (await PostAsync(client, "/contracts", new
        {
            id = contractId,
            customerId,
            responsibleDoctorId = admin.Id,
            periodStart = "2026-01-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // While the contract is DRAFT, a field visit bills at catalog price.
        var draftInvoiceId = await IssueFieldInvoiceAsync(client, admin.Id, customerId, productId);
        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.InvoiceItems.AsNoTracking().Where(i => i.InvoiceId == draftInvoiceId).Select(i => i.UnitPrice).FirstAsync())
                .Should().Be(25m, "field visits bill the catalog price");
        }

        // Activating the contract changes nothing — there is no contract pricing tier any more.
        (await PostAsync(client, $"/contracts/{contractId}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var activeInvoiceId = await IssueFieldInvoiceAsync(client, admin.Id, customerId, productId);
        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.InvoiceItems.AsNoTracking().Where(i => i.InvoiceId == activeInvoiceId).Select(i => i.UnitPrice).FirstAsync())
                .Should().Be(25m, "an active contract still bills catalog price (M29 — no contract override)");
        }
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, User user, string role = "admin")
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, role));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SyncPutAsync(HttpClient client, string table, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/sync/{table}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SyncPatchAsync(HttpClient client, string table, Guid id, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/sync/{table}/{id}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"sync-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "poultry_farm",
            fullName = "Contract Farm",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<Guid> IssueFieldInvoiceAsync(HttpClient client, Guid doctorId, Guid customerId, Guid productId)
    {
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId,
            visitType = "field",
            customerId,
            doctorId,
            status = "in_progress",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, $"/visits/{visitId}/field-invoice", new
        {
            id = invoiceId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity = 1m, discountAmount = 0m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"field-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        return invoiceId;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<User> SeedUserAsync(PgTestScope scope, string roleKey)
    {
        await using var db = NewContext(scope, null);
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = $"{roleKey} user",
            PhonePrimary = $"+9706{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"U{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Guid> SeedProductAsync(PgTestScope scope, decimal purchase, decimal selling)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;
        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId,
            EnvironmentId = scope.EnvironmentId,
            NameAr = "دواء",
            Category = ProductCategory.Medication,
            PurchasePrice = purchase,
            SellingPrice = selling,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return productId;
    }

    private static async Task SeedFieldInventoryWithStockAsync(
        PgTestScope scope, Guid doctorId, Guid productId, decimal quantity)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var fieldInventoryId = Guid.CreateVersion7();
        db.FieldInventories.Add(new FieldInventory
        {
            Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctorId, CreatedAt = now, UpdatedAt = now,
        });

        db.StockItems.Add(new StockItem
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            LocationType = StockLocation.Field,
            LocationId = fieldInventoryId,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }
}
