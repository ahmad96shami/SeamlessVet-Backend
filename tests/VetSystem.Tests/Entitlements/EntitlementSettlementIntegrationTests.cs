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

namespace VetSystem.Tests.Entitlements;

/// <summary>
/// M30 — the account lifecycle after the settlement lock was removed. Closing a customer/farm account
/// finalizes the ledger (zero-balance gate survives) but no longer computes or releases entitlements:
/// those are computed and credited to the doctor-partner ledger when a batch is <b>settled</b> (تصفية).
/// The per-visit entitlement and the approve/pay endpoints are gone.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EntitlementSettlementIntegrationTests
{
    [Fact]
    public async Task CloseAccount_ClosesLedger_WithoutComputingEntitlements()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var (batchId, farmId) = await CreateBatchAsync(client, customerId, admin.Id, feeModel: FeeModel.FixedAmount, feeValue: 20m);

        // Field sale linked to the batch, paid in full → the farm ledger nets to zero.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("cash", 250m)]);

        // Closing the batch is a plain status flip (no entitlement compute, M30).
        (await PatchAsync(client, $"/batches/{batchId}", new { status = "closed" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Close the farm account (the batch runs on a farm → its charges land on the farm ledger) →
        // the ledger is finalized, but no entitlement is computed/released.
        (await PostAsync(client, $"/farms/{farmId}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.Ledgers.AsNoTracking().Where(l => l.FarmId == farmId).Select(l => l.Status).SingleAsync())
            .Should().Be(LedgerStatus.Closed);
        (await db.DoctorEntitlements.AsNoTracking().AnyAsync(e => e.BatchId == batchId))
            .Should().BeFalse("entitlements are computed at settlement, not at batch/account close");
    }

    [Fact]
    public async Task CloseAccount_WithOutstandingBalance_IsRejected_AndLedgerStaysOpen()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var (batchId, farmId) = await CreateBatchAsync(client, customerId, admin.Id, feeModel: FeeModel.FixedAmount, feeValue: 20m);

        // Sold on credit → the farm ledger carries a debt.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("credit", 250m)]);

        var rejected = await PostAsync(client, $"/farms/{farmId}/close-account", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict, "a non-zero balance cannot be closed");
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("account_not_settled");

        await using var db = NewContext(scope, admin.Id);
        (await db.Ledgers.AsNoTracking().Where(l => l.FarmId == farmId).Select(l => l.Status).SingleAsync())
            .Should().NotBe(LedgerStatus.Closed);
    }

    [Fact]
    public async Task Approve_And_Pay_Endpoints_AreRemoved()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var anyId = Guid.CreateVersion7();
        (await PostAsync(client, $"/doctor-entitlements/{anyId}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the approve lifecycle endpoint was removed in M30");
        (await PostAsync(client, $"/doctor-entitlements/{anyId}/pay", new { method = "cash" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the pay lifecycle endpoint was removed in M30");
    }

    [Fact]
    public async Task NonBatchFieldVisit_ExamFee_CreatesNoEntitlement()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        // A completed, non-batch field visit charges a standalone exam fee — but produces no entitlement
        // (M30 removed the per-visit System-B entitlement).
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "field", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/visits/{visitId}/exam-fee-invoice", new
        {
            id = Guid.CreateVersion7(),
            amount = 80m,
            payments = new[] { new { method = "cash", amount = 80m } },
            idempotencyKey = $"exam-{visitId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/customers/{customerId}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.DoctorEntitlements.AsNoTracking().AnyAsync())
            .Should().BeFalse("a non-batch field visit's exam fee no longer creates an entitlement");
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, User admin)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body, string? idemKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idemKey ?? $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "poultry_farm",
            fullName = "Settle Farm",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<(Guid BatchId, Guid FarmId)> CreateBatchAsync(
        HttpClient client, Guid customerId, Guid doctorId, string feeModel, decimal feeValue)
    {
        var farmId = Guid.CreateVersion7();
        (await PostAsync(client, "/farms", new
        {
            id = farmId, customerId, name = $"Farm {farmId:N}"[..16], kind = "poultry",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var batchId = Guid.CreateVersion7();
        (await PostAsync(client, "/batches", new
        {
            id = batchId,
            customerId,
            farmId,
            responsibleDoctorId = doctorId,
            animalCount = 1000,
            startDate = "2026-01-01",
            supervisionFeeModel = feeModel,
            supervisionFeeValue = feeValue,
            entitlementEnabled = true,
            entitlementSystem = "drug_profit",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return (batchId, farmId);
    }

    private static async Task IssueFieldInvoiceAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid batchId, Guid productId,
        decimal quantity, (string Method, decimal Amount)[] payments)
    {
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "field", customerId, doctorId, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, $"/visits/{visitId}/field-invoice", new
        {
            id = invoiceId,
            batchId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity, discountAmount = 0m } },
            payments = payments.Select(p => new { method = p.Method, amount = p.Amount }).ToArray(),
            idempotencyKey = $"fieldinv-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<(Guid FieldInventoryId, Guid ProductId)> SeedFieldStockAsync(
        PgTestScope scope, Guid doctorId, decimal quantity, decimal purchase, decimal selling)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var fieldInventoryId = Guid.CreateVersion7();
        db.FieldInventories.Add(new FieldInventory
        {
            Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctorId,
            CreatedAt = now, UpdatedAt = now,
        });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء",
            Category = ProductCategory.Medication, PurchasePrice = purchase, SellingPrice = selling,
            CreatedAt = now, UpdatedAt = now,
        });

        db.StockItems.Add(new StockItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId,
            LocationType = StockLocation.Field, LocationId = fieldInventoryId, ProductId = productId,
            Quantity = quantity, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (fieldInventoryId, productId);
    }
}
