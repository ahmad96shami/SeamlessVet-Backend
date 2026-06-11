using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M12 task 17 + M28 — the profit-per-batch report must agree with the entitlement calculation on the
/// same inputs to the cent (exit criterion). Both flow through
/// <c>IEntitlementService.ExplainForBatchAsync</c>, so this seeds a real batch + invoice over Postgres,
/// settles it to persist the entitlement (M30 — the row is written at settlement), then asserts the
/// report's doctor/clinic split matches the persisted <c>computed_amount</c> exactly — under both
/// System A (fee carved from drug profit) and System B (fee charged on top).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProfitPerBatchReconciliationTests
{
    [Fact]
    public async Task ProfitPerBatch_ReconcilesToPersistedEntitlement_ToTheCent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id,
            feeModel: FeeModel.FixedAmount, feeValue: 20m);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("cash", 250m)]);
        await CreateDoctorPartnerAsync(client, admin.Id);
        await SettleAsync(client, batchId);

        // M28 — the supervision fee IS the entitlement: fixed fee 20 ⇒ the doctor is owed 20 in full
        // (profit = (25−10)×10 = 150 funds it; System A carves it from the clinic's margin, no clamp).
        decimal persistedShare;
        await using (var db = NewContext(scope, admin.Id))
        {
            persistedShare = (await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId)).ComputedAmount;
        }
        persistedShare.Should().Be(20m);

        var report = await GetReportAsync(client, batchId);

        report.Revenue.Should().Be(250m);
        report.DrugCost.Should().Be(100m);
        report.DrugProfit.Should().Be(150m);
        report.ExamFee.Should().Be(20m);
        report.DoctorShare.Should().Be(persistedShare, "the report must reconcile to the persisted entitlement to the cent");
        report.ClinicShare.Should().Be(150m - 20m); // 130 — System A: clinic keeps drug profit minus the fee
        report.FeeAddedToSettlement.Should().Be(0m, "System A funds the fee from the clinic margin, not the farmer");
        report.EntitlementSystem.Should().Be(EntitlementSystem.DrugProfit);
        report.EntitlementEnabled.Should().BeTrue();

        // Solo environment → no partners → the whole clinic share is retained.
        report.PartnerAllocations.Should().BeEmpty();
        report.DistributedToPartners.Should().Be(0m);
        report.RetainedByClinic.Should().Be(report.ClinicShare);
    }

    [Fact]
    public async Task ProfitPerBatch_SystemB_FeeChargedOnTop_ReportReconciles()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id,
            feeModel: FeeModel.FixedAmount, feeValue: 20m, entitlementSystem: "direct_fee");
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m,
            payments: [("cash", 250m)]);
        await CreateDoctorPartnerAsync(client, admin.Id);
        await SettleAsync(client, batchId);

        decimal persistedShare;
        await using (var db = NewContext(scope, admin.Id))
        {
            persistedShare = (await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId)).ComputedAmount;
        }
        persistedShare.Should().Be(20m, "System B credits the supervision fee to the doctor in full");

        var report = await GetReportAsync(client, batchId);

        report.DoctorShare.Should().Be(persistedShare);
        report.ExamFee.Should().Be(20m);
        report.FeeAddedToSettlement.Should().Be(20m, "System B charges the fee to the farmer on top");
        // The fee is a wash for the clinic (collected from the farmer, paid to the doctor): it keeps the full drug profit.
        report.ClinicShare.Should().Be(150m);
        report.EntitlementSystem.Should().Be(EntitlementSystem.DirectFee);
    }

    // ---- helpers ----

    private static async Task CreateDoctorPartnerAsync(HttpClient client, Guid userId)
    {
        (await PostAsync(client, "/doctor-partners", new { id = Guid.CreateVersion7(), userId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SettleAsync(HttpClient client, Guid batchId)
    {
        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 0m,
            idempotencyKey = $"settle-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<ProfitPerBatchReportResponse> GetReportAsync(HttpClient client, Guid batchId)
    {
        var resp = await client.GetAsync($"/reports/profit-per-batch?batchId={batchId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<ProfitPerBatchReportResponse>())!;
    }

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
            fullName = "Batch Profit Farm",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<Guid> CreateBatchAsync(
        HttpClient client, Guid customerId, Guid doctorId, string feeModel, decimal feeValue,
        string entitlementSystem = "drug_profit")
    {
        var batchId = Guid.CreateVersion7();
        (await PostAsync(client, "/batches", new
        {
            id = batchId,
            customerId,
            responsibleDoctorId = doctorId,
            animalCount = 1000,
            startDate = "2026-01-01",
            endDate = "2026-03-31",
            supervisionFeeModel = feeModel,
            supervisionFeeValue = feeValue,
            entitlementEnabled = true,
            entitlementSystem,
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
