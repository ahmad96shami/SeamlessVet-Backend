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
/// M24 — after a batch settlement (تصفية) every money report must tell the settled story, cent-exact.
/// Fixture: one product (cost 10) sold at TWO billed prices across two field invoices of one batch —
/// qty 10 @ 25 (250) + qty 4 @ 30 (120) → original 370 — then settled at 20 with a 25 discount:
/// repricing delta = (20−25)×10 + (20−30)×4 = −90 · settled revenue 280 · clinic revenue 255.
/// <list type="bullet">
/// <item>pharmacy re-prices the product line: revenue 20×14 = 280, cost 140 (frozen snapshot);</item>
/// <item>field visit-profit applies per-invoice deltas (200 + 80) and excludes the discount;</item>
/// <item>clinic-profits/P&amp;L add the deltas AND net the discount at settled_at:
///       revenue 370 − 90 − 25 = 255, so field profit (140) == clinic net (115) + discount (25);</item>
/// <item>doctor-income revenue moves to 280 and the share column equals the recomputed entitlement
///       50% × ((20−10)×14 − fee 20 − discount 25) = 47.50;</item>
/// <item>profit-per-batch (the entitlement engine verbatim) shows the same figures and a clinic share
///       net of the discount: 140 − 47.50 − 25 = 67.50.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class BatchSettlementReportOverlayTests
{
    [Fact]
    public async Task AllMoneyReports_SeeSettledPrices_AndStayReconciled()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);

        // Two billed prices for the same product: 10 @ 25, then (after a catalog change) 4 @ 30.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);
        await using (var db = NewContext(scope, admin.Id))
        {
            var product = await db.Products.SingleAsync(p => p.Id == productId);
            product.SellingPrice = 30m;
            await db.SaveChangesAsync();
        }
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 4m, total: 120m);

        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId, settledUnitPrice = 20m } },
            discountAmount = 25m,
            idempotencyKey = $"settle-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var clinic = await GetAsync<ClinicProfitsReportResponse>(client, "/reports/clinic-profits");
        var pnl = await GetAsync<ProfitAndLossResponse>(client, "/reports/profit-and-loss");
        var pharmacy = await GetAsync<PharmacyProfitReportResponse>(client, "/reports/pharmacy-profit");
        var field = await GetAsync<VisitProfitReportResponse>(client, "/reports/field-visit-profit");
        var income = await GetAsync<DoctorIncomeReportResponse>(client, "/reports/doctor-income");
        var perBatch = await GetAsync<ProfitPerBatchReportResponse>(client, $"/reports/profit-per-batch?batchId={batchId}");

        // Clinic-profits: settled revenue minus the discount; COGS untouched (cost snapshots).
        clinic.Revenue.Should().Be(255m, "370 billed − 90 reprice − 25 discount");
        clinic.Cogs.Should().Be(140m);
        clinic.NetProfit.Should().Be(115m);
        clinic.SettlementDiscounts.Should().Be(25m);

        // P&L mirrors clinic-profits exactly.
        pnl.Revenue.Should().Be(clinic.Revenue);
        pnl.Cogs.Should().Be(clinic.Cogs);
        pnl.GrossProfit.Should().Be(clinic.NetProfit);
        pnl.SettlementDiscounts.Should().Be(25m);

        // Pharmacy: the product line at its negotiated price.
        pharmacy.Revenue.Should().Be(280m, "20 × 14");
        pharmacy.Cost.Should().Be(140m);
        pharmacy.Profit.Should().Be(140m);
        pharmacy.Cost.Should().Be(clinic.Cogs, "the M20 cost reconciliation must survive settlement");

        // Field visit-profit: per-invoice deltas, no discount (it has no per-visit basis).
        field.VisitCount.Should().Be(2);
        field.Revenue.Should().Be(280m, "(250−50) + (120−40)");
        field.Cogs.Should().Be(140m);
        field.Profit.Should().Be(140m);
        field.Revenue.Should().Be(clinic.Revenue + clinic.SettlementDiscounts,
            "the visit slices exclude the batch discount; clinic-profits nets it at settled_at");

        // Doctor income: revenue at settled prices; the share column equals the recomputed entitlement.
        var doctorRow = income.Rows.Single(r => r.DoctorId == admin.Id);
        doctorRow.TotalRevenue.Should().Be(280m);
        doctorRow.CalculatedShare.Should().Be(47.50m, "50% × ((20−10)×14 − 20 − 25)");

        // Profit-per-batch (the entitlement engine verbatim): the full settled accounting.
        perBatch.Revenue.Should().Be(280m);
        perBatch.DrugCost.Should().Be(140m);
        perBatch.DrugProfit.Should().Be(140m);
        perBatch.ExamFee.Should().Be(20m);
        perBatch.DoctorShare.Should().Be(47.50m);
        perBatch.SettlementDiscount.Should().Be(25m);
        perBatch.ClinicShare.Should().Be(67.50m, "(drugProfit 140 − discount 25) == doctor 47.50 + clinic 67.50");
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

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {path} should succeed");
        return (await response.Content.ReadFromJsonAsync<T>())!;
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

    private static async Task<(Guid CustomerId, Guid FarmId, Guid BatchId)> SeedFarmBatchAsync(
        HttpClient client, Guid doctorId)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "poultry_farm",
            fullName = "Overlay Farm Co",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

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
            supervisionFeeModel = FeeModel.FixedAmount,
            supervisionFeeValue = 20m,
            entitlementEnabled = true,
            entitlementSystem = "drug_profit",
            doctorSharePercent = 50m,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        return (customerId, farmId, batchId);
    }

    private static async Task IssueFieldInvoiceAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid farmId, Guid batchId, Guid productId,
        decimal quantity, decimal total)
    {
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "field", customerId, farmId, doctorId, status = "in_progress",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, $"/visits/{visitId}/field-invoice", new
        {
            id = invoiceId,
            batchId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity, discountAmount = 0m } },
            payments = new[] { new { method = "credit", amount = total } },
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

    private static async Task<Guid> SeedFieldStockAsync(PgTestScope scope, Guid doctorId, decimal selling)
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
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء التصفية",
            Category = ProductCategory.Medication, PurchasePrice = 10m, SellingPrice = selling,
            CreatedAt = now, UpdatedAt = now,
        });

        db.StockItems.Add(new StockItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId,
            LocationType = StockLocation.Field, LocationId = fieldInventoryId, ProductId = productId,
            Quantity = 100m, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return productId;
    }
}
