using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M25 exit criteria over a real Postgres env: two receipts at different costs/expiries create two
/// lots; a sale consumes the earlier-expiry lot first and snapshots the weighted-average cost into
/// <c>invoice_items.cost_price</c>; <c>Σ inventory_lots.remaining_qty == stock_items.quantity</c>
/// after sales and field transfers; and the near-expiry scan reports per lot (near-expiry qty vs
/// total on hand).
/// </summary>
[Trait("Category", "Integration")]
public sealed class FefoCostingIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TwoLots_SaleConsumesEarliestExpiryFirst_AndCostPriceIsWeightedAverage()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndProductAsync(scope, purchase: 5m, selling: 30m);
        // No clock override: a JWT signed off a fake clock would read as expired to the real-clock
        // bearer validator. These assertions (FEFO order + weighted-avg cost) are clock-independent.
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // Lot A: dearer, expires sooner → must be consumed first. Lot B: cheaper, expires later.
        await ReceiveAsync(client, productId, warehouseId, qty: 10m, unitCost: 12m, expiry: new DateOnly(2026, 7, 1));
        await ReceiveAsync(client, productId, warehouseId, qty: 10m, unitCost: 9m, expiry: new DateOnly(2026, 12, 1));

        await using (var db = NewContext(scope, admin.Id))
        {
            var lots = await db.InventoryLots.AsNoTracking().Where(l => l.ProductId == productId).ToListAsync();
            lots.Should().HaveCount(2, "each receipt is its own lot");
            (await StockAsync(db, warehouseId, productId)).Should().Be(20m);
        }

        // Sale 1 of 4 — wholly inside the earlier-expiry lot A → cost is exactly lot A's cost.
        var inv1 = await SellPosAsync(client, productId, qty: 4m, unitPrice: 30m);
        await using (var db = NewContext(scope, admin.Id))
        {
            var line = await db.InvoiceItems.AsNoTracking().SingleAsync(it => it.InvoiceId == inv1 && it.ProductId == productId);
            line.CostPrice.Should().Be(12m, "FEFO drains the earlier-expiry lot (cost 12) first");
        }

        // Sale 2 of 8 — drains lot A's remaining 6 (@12) then 2 from lot B (@9): (6×12 + 2×9)/8 = 11.25.
        var inv2 = await SellPosAsync(client, productId, qty: 8m, unitPrice: 30m);
        await using (var db = NewContext(scope, admin.Id))
        {
            var line = await db.InvoiceItems.AsNoTracking().SingleAsync(it => it.InvoiceId == inv2 && it.ProductId == productId);
            line.CostPrice.Should().Be(11.25m, "the line cost is the weighted-average of the lots the sale consumed");

            // Lot ledger reconciles to the materialized balance after both sales.
            var stock = await StockAsync(db, warehouseId, productId);
            var lotRemaining = await db.InventoryLots.AsNoTracking()
                .Where(l => l.ProductId == productId).SumAsync(l => l.RemainingQty);
            stock.Should().Be(8m, "20 received − 4 − 8 sold");
            lotRemaining.Should().Be(stock, "Σ remaining_qty must equal stock_items.quantity");
        }
    }

    [Fact]
    public async Task FieldTransfer_MirrorsLotCost_AndKeepsLotEqualToStock_AtBothLocations()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, fieldInventoryId, productId) =
            await SeedWarehouseFieldAndProductAsync(scope, admin.Id, purchase: 5m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        await ReceiveAsync(client, productId, warehouseId, qty: 10m, unitCost: 8m, expiry: new DateOnly(2026, 10, 1));

        // Load 6 to the field — the destination must mirror the consumed source lot's cost + expiry.
        await TransferAsync(client, MovementType.LoadToField, productId, qty: 6m,
            fromType: StockLocation.Warehouse, fromId: warehouseId,
            toType: StockLocation.Field, toId: fieldInventoryId);

        await using var db = NewContext(scope, admin.Id);

        var warehouseStock = await StockAsync(db, warehouseId, productId);
        var fieldStock = await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == StockLocation.Field && s.LocationId == fieldInventoryId && s.ProductId == productId)
            .Select(s => s.Quantity).FirstAsync();
        warehouseStock.Should().Be(4m);
        fieldStock.Should().Be(6m);

        var warehouseLots = await db.InventoryLots.AsNoTracking()
            .Where(l => l.ProductId == productId && l.LocationType == StockLocation.Warehouse).ToListAsync();
        var fieldLots = await db.InventoryLots.AsNoTracking()
            .Where(l => l.ProductId == productId && l.LocationType == StockLocation.Field).ToListAsync();

        warehouseLots.Sum(l => l.RemainingQty).Should().Be(warehouseStock, "warehouse Σ remaining == warehouse stock");
        fieldLots.Sum(l => l.RemainingQty).Should().Be(fieldStock, "field Σ remaining == field stock");
        fieldLots.Should().OnlyContain(l => l.UnitCost == 8m, "the transfer mirrors the source lot's cost");
        fieldLots.Should().OnlyContain(l => l.ExpirationDate == new DateOnly(2026, 10, 1), "and its expiry");
    }

    [Fact]
    public async Task NearExpiryScan_ReportsPerLot_WithNearExpiryQtyAndTotalOnHand()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndProductAsync(scope, purchase: 5m, selling: 30m);

        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        await using (var seed = NewContext(scope, admin.Id))
        {
            // 10 units expiring in 10 days (inside the 30-day default window) + 10 expiring in 200 days.
            seed.InventoryLots.Add(Lot(scope, warehouseId, productId, qty: 10m, cost: 5m, expiry: today.AddDays(10)));
            seed.InventoryLots.Add(Lot(scope, warehouseId, productId, qty: 10m, cost: 5m, expiry: today.AddDays(200)));
            seed.StockItems.Add(new StockItem
            {
                Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId,
                LocationType = StockLocation.Warehouse, LocationId = warehouseId, ProductId = productId, Quantity = 20m,
            });
            await seed.SaveChangesAsync();
        }

        await using var factory = new VetApiFactory { Clock = new FakeClock(Now) };
        using var diScope = factory.Services.CreateScope();
        var scan = diScope.ServiceProvider.GetRequiredService<IInventoryScanService>();

        var expiring = await scan.ScanApproachingExpirationAsync(scope.EnvironmentId, CancellationToken.None);

        expiring.Should().ContainSingle("only the near-expiry lot is within the warning window");
        var row = expiring[0];
        row.ProductId.Should().Be(productId);
        row.NearExpiryQuantity.Should().Be(10m, "the near-expiry quantity is that lot's remaining");
        row.QuantityOnHand.Should().Be(20m, "total on hand spans both lots");
        row.DaysUntilExpiry.Should().Be(10);
    }

    // ---- helpers ----

    private static InventoryLot Lot(PgTestScope scope, Guid warehouseId, Guid productId, decimal qty, decimal cost, DateOnly expiry)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            ProductId = productId,
            LocationType = StockLocation.Warehouse,
            LocationId = warehouseId,
            UnitCost = cost,
            ExpirationDate = expiry,
            ReceivedQty = qty,
            RemainingQty = qty,
            ReceivedAt = Now,
        };

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
        var resp = await PutMovementAsync(client, new
        {
            id = Guid.CreateVersion7(),
            product_id = productId,
            movement_type = MovementType.Receive,
            to_location_type = StockLocation.Warehouse,
            to_location_id = warehouseId,
            quantity_delta = qty,
            unit_cost = unitCost,
            expiration_date = expiry.ToString("yyyy-MM-dd"),
            idempotency_key = $"rcv-{Guid.NewGuid():N}"[..32],
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static async Task TransferAsync(
        HttpClient client, string movementType, Guid productId, decimal qty,
        string fromType, Guid fromId, string toType, Guid toId)
    {
        var resp = await PutMovementAsync(client, new
        {
            id = Guid.CreateVersion7(),
            product_id = productId,
            movement_type = movementType,
            from_location_type = fromType,
            from_location_id = fromId,
            to_location_type = toType,
            to_location_id = toId,
            quantity_delta = qty,
            idempotency_key = $"xfer-{Guid.NewGuid():N}"[..32],
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PutMovementAsync(HttpClient client, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/sync/inventory_movements")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", $"mv-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> SellPosAsync(HttpClient client, Guid productId, decimal qty, decimal unitPrice)
    {
        var invoiceId = Guid.CreateVersion7();
        var request = new HttpRequestMessage(HttpMethod.Post, "/pos/invoices")
        {
            Content = JsonContent.Create(new
            {
                id = invoiceId,
                customerId = (Guid?)null, // walk-in: exercises the sale path without ledger noise
                discountAmount = 0m,
                items = new[] { new { productId, quantity = qty, unitPrice, discountAmount = 0m } },
                payments = new[] { new { method = "cash", amount = qty * unitPrice } },
                idempotencyKey = $"pos-{invoiceId:N}",
            }),
        };
        request.Headers.Add("Idempotency-Key", $"pos-{Guid.NewGuid():N}"[..32]);
        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return invoiceId;
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

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseAndProductAsync(
        PgTestScope scope, decimal purchase, decimal selling)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء",
            Category = ProductCategory.Medication, PurchasePrice = purchase, SellingPrice = selling,
            CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }

    private static async Task<(Guid WarehouseId, Guid FieldInventoryId, Guid ProductId)> SeedWarehouseFieldAndProductAsync(
        PgTestScope scope, Guid doctorId, decimal purchase)
    {
        var (warehouseId, productId) = await SeedWarehouseAndProductAsync(scope, purchase, selling: purchase * 2m);

        await using var db = NewContext(scope, null);
        var fieldInventoryId = Guid.CreateVersion7();
        db.FieldInventories.Add(new FieldInventory
        {
            Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctorId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (warehouseId, fieldInventoryId, productId);
    }
}
