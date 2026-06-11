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

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M27 exit criteria over a real Postgres env: recording an internal-use consumption decrements the
/// correct FEFO lot(s) + the aggregate balance and writes a <c>consume</c> movement snapshotting the
/// consumed weighted-average cost; the consumables report sums consumption (qty + cost); a
/// non-consumable product is rejected; and the negative-stock guard still fires.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConsumablesIntegrationTests
{
    [Fact]
    public async Task Consume_FefoDecrementsLots_WritesConsumeMovement_AndReportSumsQtyAndCost()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndConsumableAsync(scope, purchase: 5m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // Lot A: dearer, expires sooner → consumed first. Lot B: cheaper, expires later.
        await ReceiveAsync(client, productId, warehouseId, qty: 10m, unitCost: 12m, expiry: new DateOnly(2026, 7, 1));
        await ReceiveAsync(client, productId, warehouseId, qty: 10m, unitCost: 9m, expiry: new DateOnly(2026, 12, 1));

        // Consume 4 — wholly inside lot A (cost 12). Then consume 8 — drains A's remaining 6 (@12)
        // then 2 from B (@9): weighted average (6×12 + 2×9)/8 = 11.25.
        await ConsumeAsync(client, productId, qty: 4m, reason: "قفازات وحقن");
        await ConsumeAsync(client, productId, qty: 8m, reason: "قفازات وحقن");

        await using (var db = NewContext(scope, admin.Id))
        {
            // Aggregate + lot ledger reconcile after both consumptions.
            var stock = await StockAsync(db, warehouseId, productId);
            stock.Should().Be(8m, "20 received − 4 − 8 consumed");
            var lotRemaining = await db.InventoryLots.AsNoTracking()
                .Where(l => l.ProductId == productId).SumAsync(l => l.RemainingQty);
            lotRemaining.Should().Be(stock, "Σ remaining_qty must equal stock_items.quantity");

            // The earlier-expiry lot drained first.
            var lots = await db.InventoryLots.AsNoTracking()
                .Where(l => l.ProductId == productId).OrderBy(l => l.ExpirationDate).ToListAsync();
            lots[0].RemainingQty.Should().Be(0m, "lot A (expires 2026-07-01) is fully consumed");
            lots[1].RemainingQty.Should().Be(8m, "lot B keeps 8 of 10");

            // Two consume movements, each snapshotting its FEFO weighted-average unit cost.
            var moves = await db.InventoryMovements.AsNoTracking()
                .Where(m => m.ProductId == productId && m.MovementType == MovementType.Consume)
                .OrderBy(m => m.CreatedAt).ToListAsync();
            moves.Should().HaveCount(2);
            moves.Should().OnlyContain(m => m.QuantityDelta < 0m, "a consume is a negative-delta deduction");
            moves[0].QuantityDelta.Should().Be(-4m);
            moves[0].UnitCost.Should().Be(12m, "the first consumption drew only lot A");
            moves[1].QuantityDelta.Should().Be(-8m);
            moves[1].UnitCost.Should().Be(11.25m, "the second consumption's weighted-average cost");
        }

        // The consumables report sums consumption: qty 12, cost 4×12 + 8×11.25 = 138.
        var report = (await client.GetFromJsonAsync<ConsumablesReportResponse>("/reports/consumables"))!;
        report.TotalQuantity.Should().Be(12m);
        report.TotalCost.Should().Be(138m, "Σ qty × unit_cost across both consumptions");
        report.Rows.Should().ContainSingle();
        var row = report.Rows[0];
        row.ProductId.Should().Be(productId);
        row.LocationType.Should().Be(StockLocation.Warehouse);
        row.Quantity.Should().Be(12m);
        row.Cost.Should().Be(138m);
    }

    [Fact]
    public async Task Consume_NonConsumableProduct_IsRejected()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        // A plain (non-consumable) product — the consume guard must refuse it.
        var (warehouseId, productId) = await SeedWarehouseAndConsumableAsync(scope, purchase: 5m, isConsumable: false);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        await ReceiveAsync(client, productId, warehouseId, qty: 10m, unitCost: 5m, expiry: new DateOnly(2026, 10, 1));

        var resp = await PostConsumeAsync(client, productId, qty: 1m, reason: "خطأ");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("product_not_consumable");
    }

    [Fact]
    public async Task Consume_BeyondOnHand_FiresNegativeStockGuard()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndConsumableAsync(scope, purchase: 5m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        await ReceiveAsync(client, productId, warehouseId, qty: 3m, unitCost: 5m, expiry: new DateOnly(2026, 10, 1));

        var resp = await PostConsumeAsync(client, productId, qty: 5m, reason: "أكثر من المتوفر");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("negative_stock");

        // Nothing persisted — the guard rolls back the whole write.
        await using var db = NewContext(scope, admin.Id);
        (await StockAsync(db, warehouseId, productId)).Should().Be(3m, "the rejected consumption left stock untouched");
        (await db.InventoryMovements.AsNoTracking()
            .CountAsync(m => m.ProductId == productId && m.MovementType == MovementType.Consume))
            .Should().Be(0);
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

    private static async Task ReceiveAsync(
        HttpClient client, Guid productId, Guid warehouseId, decimal qty, decimal unitCost, DateOnly expiry)
    {
        var resp = await PostInventoryAsync(client, "/inventory/receive", new
        {
            id = Guid.CreateVersion7(),
            productId,
            quantity = qty,
            warehouseId,
            unitCost,
            expirationDate = expiry.ToString("yyyy-MM-dd"),
            idempotencyKey = Key("rcv"),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static async Task ConsumeAsync(HttpClient client, Guid productId, decimal qty, string reason)
    {
        var resp = await PostConsumeAsync(client, productId, qty, reason);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostConsumeAsync(HttpClient client, Guid productId, decimal qty, string reason)
        => PostInventoryAsync(client, "/inventory/consume", new
        {
            id = Guid.CreateVersion7(),
            productId,
            quantity = qty,
            reason,
            idempotencyKey = Key("csm"),
        });

    private static async Task<HttpResponseMessage> PostInventoryAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Key("hdr"));
        return await client.SendAsync(request);
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

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseAndConsumableAsync(
        PgTestScope scope, decimal purchase, bool isConsumable = true)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "قفازات",
            Category = ProductCategory.Product, PurchasePrice = purchase, SellingPrice = purchase * 2m,
            IsConsumable = isConsumable, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }

    private static string Key(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..32];
}
