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
/// M9 tasks 18–21 + exit criteria — the settlement lifecycle against the API over a real Postgres env:
/// batch close computes a pending entitlement, the settlement lock blocks approval until the customer
/// account closes in full, only a zero balance closes it, and approve→pay then disburses. System B is
/// exercised via a standalone exam-fee invoice.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EntitlementSettlementIntegrationTests
{
    [Fact]
    public async Task FullLifecycle_BatchDrugProfit_CloseBatch_CloseAccount_Approve_Pay()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id,
            feeModel: FeeModel.FixedAmount, feeValue: 20m);

        // Field visit → field invoice linked to the batch, paid in full (ledger nets to zero).
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("cash", 250m)]); // 10 × 25 = 250 → fully settled

        // Close the cycle: computes the responsible doctor's entitlement as pending (PRD §7.8).
        (await PatchAsync(client, $"/batches/{batchId}", new { status = "closed" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        Guid entitlementId;
        await using (var db = NewContext(scope, admin.Id))
        {
            var entitlement = await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId);
            entitlement.Status.Should().Be(EntitlementStatus.Pending);
            entitlement.CalculationSystem.Should().Be(EntitlementSystem.DrugProfit);
            entitlement.DoctorId.Should().Be(admin.Id);
            // M28 — the supervision fee IS the entitlement: fixed fee 20 ⇒ the doctor is owed 20 in full
            // (drug profit (25 − 10) × 10 = 150 funds it from the clinic's margin under System A).
            entitlement.ComputedAmount.Should().Be(20m);
            entitlementId = entitlement.Id;
        }

        // Account fully settled → close it, then approve + pay the entitlement.
        (await PostAsync(client, $"/customers/{customerId}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/doctor-entitlements/{entitlementId}/approve", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/doctor-entitlements/{entitlementId}/pay", new { method = "bank_transfer" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = NewContext(scope, admin.Id);
        var paid = await verify.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.Id == entitlementId);
        paid.Status.Should().Be(EntitlementStatus.Paid);
        paid.ApprovedBy.Should().Be(admin.Id);
        paid.ApprovedAt.Should().NotBeNull();
        paid.PaidAt.Should().NotBeNull();
        paid.PaidMethod.Should().Be(PaymentMethod.BankTransfer);

        (await verify.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Status).SingleAsync())
            .Should().Be(LedgerStatus.Closed);
    }

    [Fact]
    public async Task SystemB_StandaloneExamFee_CreditsDoctorInFull_OnAccountClose()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        // A completed, non-batch visit with a standalone exam-fee invoice, paid in full.
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

        // Close the account → the settlement workflow computes the System B visit entitlement.
        (await PostAsync(client, $"/customers/{customerId}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var entitlement = await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.VisitId == visitId);
        entitlement.CalculationSystem.Should().Be(EntitlementSystem.DirectFee);
        entitlement.ComputedAmount.Should().Be(80m, "System B credits the full exam fee to the doctor");
    }

    [Fact]
    public async Task CloseAccount_WithOutstandingBalance_IsRejected_EntitlementStaysPending()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id,
            feeModel: FeeModel.FixedAmount, feeValue: 20m);

        // Sold on credit → the ledger carries a debt.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("credit", 250m)]);

        (await PatchAsync(client, $"/batches/{batchId}", new { status = "closed" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var rejected = await PostAsync(client, $"/customers/{customerId}/close-account", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict, "a non-zero balance cannot be closed (partial payments do not release)");

        await using var db = NewContext(scope, admin.Id);
        (await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId)).Status
            .Should().Be(EntitlementStatus.Pending);
        (await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Status).SingleAsync())
            .Should().NotBe(LedgerStatus.Closed);
    }

    [Fact]
    public async Task PartialPayment_ThenApprove_IsRejected_OnlyFullSettlementReleases()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id,
            feeModel: FeeModel.FixedAmount, feeValue: 20m);

        // 250 total, only 100 paid → 150 still outstanding.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("cash", 100m)]);
        (await PatchAsync(client, $"/batches/{batchId}", new { status = "closed" })).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid entitlementId;
        await using (var db = NewContext(scope, admin.Id))
        {
            entitlementId = (await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId)).Id;
        }

        // Partial settlement does NOT release: approval is locked.
        (await PostAsync(client, $"/doctor-entitlements/{entitlementId}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "the account is not closed");

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.Id == entitlementId)).Status
                .Should().Be(EntitlementStatus.Pending);
        }

        // Pay the remaining 150 (Sanad Qabd) → balance zero → account closes → approval released.
        (await PostAsync(client, "/receipt-vouchers", new
        {
            id = Guid.CreateVersion7(),
            customerId,
            amount = 150m,
            method = "cash",
            idempotencyKey = $"rv-{customerId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/customers/{customerId}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/doctor-entitlements/{entitlementId}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the account is now closed in full");

        await using var verify = NewContext(scope, admin.Id);
        (await verify.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.Id == entitlementId)).Status
            .Should().Be(EntitlementStatus.Approved);
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

    private static async Task<Guid> CreateBatchAsync(
        HttpClient client, Guid customerId, Guid doctorId, string feeModel, decimal feeValue)
    {
        var batchId = Guid.CreateVersion7();
        (await PostAsync(client, "/batches", new
        {
            id = batchId,
            customerId,
            responsibleDoctorId = doctorId,
            animalCount = 1000,
            startDate = "2026-01-01",
            supervisionFeeModel = feeModel,
            supervisionFeeValue = feeValue,
            entitlementEnabled = true,
            entitlementSystem = "drug_profit",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return batchId;
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
