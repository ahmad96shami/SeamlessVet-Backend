using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Vaccinations;

/// <summary>
/// M26 exit criteria over a real Postgres env: a vaccine is a stock product — purchasable
/// (lot/cost/expiry), administering it FEFO-deducts a dose and captures the lot's cost, and billing
/// assembles it as a product line at most once whose <c>cost_price</c> is the captured FEFO cost (so
/// profit/COGS reconciles), with no re-deduct. The next-due reminder still finds it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VaccineStockIntegrationTests
{
    [Fact]
    public async Task VaccinePurchase_ToLots_Administer_FefoDeduct_BillsProductLineOnce_AndProfitReconciles()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, vaccineProductId) = await SeedWarehouseAndVaccineAsync(scope, purchase: 8m, selling: 30m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var supplierId = await CreateSupplierAsync(client);

        // Purchase → two lots at different costs/expiries. Lot A is dearer + expires sooner → FEFO first.
        await PurchaseAsync(client, supplierId, vaccineProductId, qty: 5m, unitCost: 12m, expiry: "2026-07-01");
        await PurchaseAsync(client, supplierId, vaccineProductId, qty: 5m, unitCost: 9m, expiry: "2026-12-01");

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.InventoryLots.AsNoTracking().CountAsync(l => l.ProductId == vaccineProductId))
                .Should().Be(2, "each purchase line seeds its own lot");
            (await StockAsync(db, warehouseId, vaccineProductId)).Should().Be(10m);
        }

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Administer the vaccine — FEFO-deducts one dose from the earlier-expiry lot (@12), no price
        // given so it snapshots the product's selling price (30). next_due_date drives the reminder.
        var vaccinationId = Guid.CreateVersion7();
        (await PostAsync(client, "/vaccinations", new
        {
            id = vaccinationId, customerId, visitId, productId = vaccineProductId,
            vaccineType = "لقاح السعار", dateGiven = "2026-06-01", nextDueDate = "2026-09-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var vax = await db.Vaccinations.AsNoTracking().SingleAsync(v => v.Id == vaccinationId);
            vax.ProductId.Should().Be(vaccineProductId);
            vax.Price.Should().Be(30m, "the snapshot defaults to the product's selling price");
            vax.ResolvedUnitCost.Should().Be(12m, "FEFO captured the earlier-expiry lot's cost at administration");

            var stock = await StockAsync(db, warehouseId, vaccineProductId);
            stock.Should().Be(9m, "administering deducted one dose");
            (await db.InventoryLots.AsNoTracking().Where(l => l.ProductId == vaccineProductId).SumAsync(l => l.RemainingQty))
                .Should().Be(stock, "Σ remaining_qty == stock_items.quantity after the FEFO draw");
            (await db.InventoryLots.AsNoTracking().Where(l => l.ProductId == vaccineProductId && l.UnitCost == 12m).SumAsync(l => l.RemainingQty))
                .Should().Be(4m, "the earlier-expiry lot (cost 12) gave up the dose");
        }

        // Bill it — the visit-linked POS auto-assembles the vaccination as a PRODUCT line; the stock
        // already moved at administration, so issuance snapshots the captured cost and does NOT re-deduct.
        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"vaxbill-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var line = await db.InvoiceItems.AsNoTracking().SingleAsync(it => it.InvoiceId == invoiceId);
            line.VaccinationId.Should().Be(vaccinationId);
            line.ProductId.Should().Be(vaccineProductId, "a vaccination bills as a product line (M26)");
            line.ServiceId.Should().BeNull();
            line.Quantity.Should().Be(1m);
            line.UnitPrice.Should().Be(30m);
            line.CostPrice.Should().Be(12m, "COGS is the FEFO cost captured at administration");
            line.LineTotal.Should().Be(30m);
            (line.UnitPrice - line.CostPrice).Should().Be(18m, "profit = price − captured FEFO cost reconciles");

            (await StockAsync(db, warehouseId, vaccineProductId))
                .Should().Be(9m, "billing must NOT re-deduct — the dose moved at administration");
        }

        // Billed once — a second visit-linked POS has nothing left to assemble.
        var rebillId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = rebillId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"vaxrebill-{rebillId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict, "the vaccination is already billed");

        // Next-due reminder unaffected: the administered vaccination still surfaces in the upcoming scan.
        var upcoming = await client.GetFromJsonAsync<JsonElement>("/vaccinations/upcoming?from=2026-01-01&to=2026-12-31");
        upcoming.EnumerateArray().Select(v => v.GetProperty("id").GetGuid())
            .Should().Contain(vaccinationId, "a product-linked vaccination with a due date still drives reminders");
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
        request.Headers.Add("Idempotency-Key", idemKey ?? $"vt-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task PurchaseAsync(
        HttpClient client, Guid supplierId, Guid productId, decimal qty, decimal unitCost, string expiry)
    {
        var purchaseId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/purchase-invoices", new
        {
            id = purchaseId,
            supplierId,
            number = $"VAX-{purchaseId.ToString("N")[..8]}",
            discountAmount = 0m,
            taxAmount = (decimal?)null,
            items = new[] { new { productId, quantity = qty, unitCost, discountAmount = 0m, expirationDate = expiry } },
            idempotencyKey = $"pi-{purchaseId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateSupplierAsync(HttpClient client)
    {
        var supplierId = Guid.CreateVersion7();
        (await PostAsync(client, "/suppliers", new
        {
            id = supplierId, name = "Vaccine Distributor",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return supplierId;
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Vax Cust",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<decimal> StockAsync(ApplicationDbContext db, Guid warehouseId, Guid productId)
        => await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == StockLocation.Warehouse && s.LocationId == warehouseId && s.ProductId == productId)
            .Select(s => s.Quantity).FirstAsync();

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseAndVaccineAsync(
        PgTestScope scope, decimal purchase, decimal selling)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "لقاح السعار",
            Category = ProductCategory.Vaccine, PurchasePrice = purchase, SellingPrice = selling,
            CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }
}
