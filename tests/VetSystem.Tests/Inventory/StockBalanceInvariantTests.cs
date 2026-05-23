using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M4 task 18 — property-style test: for any sequence of movements, every (location, product)
/// <c>stock_items.quantity</c> equals the signed sum of <c>inventory_movements.quantity_delta</c>
/// over the rows whose affected location is that pair (affected = <c>to</c> when delta &gt; 0, else
/// <c>from</c>). Stand-in for the formal FsCheck/CsCheck suite that lands with M13 (mirrors the M3
/// ledger balance-invariant test). The generator caps deductions/transfers at available stock so
/// every movement succeeds; an independently-tracked expected balance is cross-checked too.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StockBalanceInvariantTests
{
    [Theory]
    [InlineData(7, 45)]
    [InlineData(123, 60)]
    [InlineData(2024, 40)]
    public async Task RandomMovementSequence_PreservesStockEqualsSumOfDeltas(int seed, int iterations)
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, fieldInventoryId, productId) = await SeedLocationsAndProductAsync(scope, admin.Id);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var rng = new Random(seed);
        var expectedWarehouse = 0m;
        var expectedField = 0m;

        for (var i = 0; i < iterations; i++)
        {
            switch (rng.Next(6))
            {
                case 0: // receive into warehouse
                {
                    var qty = Qty(rng, 1m, 50m);
                    await PutMovementAsync(client, MovementType.Receive, productId, qty,
                        toType: StockLocation.Warehouse, toId: warehouseId);
                    expectedWarehouse += qty;
                    break;
                }
                case 1: // return_add to a random location
                {
                    var toField = rng.Next(2) == 0;
                    var qty = Qty(rng, 1m, 20m);
                    await PutMovementAsync(client, MovementType.ReturnAdd, productId, qty,
                        toType: toField ? StockLocation.Field : StockLocation.Warehouse,
                        toId: toField ? fieldInventoryId : warehouseId);
                    if (toField) expectedField += qty; else expectedWarehouse += qty;
                    break;
                }
                case 2: // sale_deduct from a location that has stock
                {
                    var fromField = rng.Next(2) == 0;
                    var available = fromField ? expectedField : expectedWarehouse;
                    var qty = Fraction(rng, available);
                    if (qty <= 0m) continue;
                    await PutMovementAsync(client, MovementType.SaleDeduct, productId, qty,
                        fromType: fromField ? StockLocation.Field : StockLocation.Warehouse,
                        fromId: fromField ? fieldInventoryId : warehouseId);
                    if (fromField) expectedField -= qty; else expectedWarehouse -= qty;
                    break;
                }
                case 3: // signed adjust at a location (negative capped at available)
                {
                    var atField = rng.Next(2) == 0;
                    var available = atField ? expectedField : expectedWarehouse;
                    var positive = rng.Next(2) == 0;
                    var delta = positive ? Qty(rng, 1m, 15m) : -Fraction(rng, available);
                    if (delta == 0m) continue;
                    await PutMovementAsync(client, MovementType.Adjust, productId, delta,
                        toType: atField ? StockLocation.Field : StockLocation.Warehouse,
                        toId: atField ? fieldInventoryId : warehouseId);
                    if (atField) expectedField += delta; else expectedWarehouse += delta;
                    break;
                }
                case 4: // load_to_field (needs warehouse stock)
                {
                    var qty = Fraction(rng, expectedWarehouse);
                    if (qty <= 0m) continue;
                    await PutMovementAsync(client, MovementType.LoadToField, productId, qty,
                        fromType: StockLocation.Warehouse, fromId: warehouseId,
                        toType: StockLocation.Field, toId: fieldInventoryId);
                    expectedWarehouse -= qty;
                    expectedField += qty;
                    break;
                }
                default: // unload_from_field (needs field stock)
                {
                    var qty = Fraction(rng, expectedField);
                    if (qty <= 0m) continue;
                    await PutMovementAsync(client, MovementType.UnloadFromField, productId, qty,
                        fromType: StockLocation.Field, fromId: fieldInventoryId,
                        toType: StockLocation.Warehouse, toId: warehouseId);
                    expectedField -= qty;
                    expectedWarehouse += qty;
                    break;
                }
            }
        }

        await using var verify = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var actualWarehouse = await StockQuantityAsync(verify, StockLocation.Warehouse, warehouseId, productId);
        var actualField = await StockQuantityAsync(verify, StockLocation.Field, fieldInventoryId, productId);

        var movements = await verify.InventoryMovements
            .AsNoTracking()
            .Where(m => m.ProductId == productId)
            .ToListAsync();

        var sumWarehouse = SumOfDeltasAt(movements, StockLocation.Warehouse, warehouseId);
        var sumField = SumOfDeltasAt(movements, StockLocation.Field, fieldInventoryId);

        actualWarehouse.Should().Be(expectedWarehouse, "warehouse balance must track the applied deltas");
        actualField.Should().Be(expectedField, "field balance must track the applied deltas");

        actualWarehouse.Should().Be(sumWarehouse,
            "stock_items.quantity must equal Σ quantity_delta over the warehouse's movements");
        actualField.Should().Be(sumField,
            "stock_items.quantity must equal Σ quantity_delta over the field's movements");
    }

    private static decimal Qty(Random rng, decimal min, decimal max)
        => Math.Round(min + ((decimal)rng.NextDouble() * (max - min)), 3, MidpointRounding.AwayFromZero);

    /// <summary>A positive fraction (≤ available, 3 dp). Returns 0 when nothing is available.</summary>
    private static decimal Fraction(Random rng, decimal available)
    {
        if (available <= 0m) return 0m;
        return Math.Round(available * (decimal)(0.1 + (rng.NextDouble() * 0.8)), 3, MidpointRounding.ToZero);
    }

    private static decimal SumOfDeltasAt(IEnumerable<InventoryMovement> movements, string locationType, Guid locationId)
        => movements
            .Where(m => (m.QuantityDelta >= 0 ? m.ToLocationType : m.FromLocationType) == locationType
                        && (m.QuantityDelta >= 0 ? m.ToLocationId : m.FromLocationId) == locationId)
            .Sum(m => m.QuantityDelta);

    private static async Task<decimal> StockQuantityAsync(
        VetSystem.Infrastructure.Persistence.ApplicationDbContext db,
        string locationType, Guid locationId, Guid productId)
        => await db.StockItems
            .AsNoTracking()
            .Where(s => s.LocationType == locationType && s.LocationId == locationId && s.ProductId == productId)
            .Select(s => (decimal?)s.Quantity)
            .FirstOrDefaultAsync() ?? 0m;

    private static async Task PutMovementAsync(
        HttpClient client,
        string movementType,
        Guid productId,
        decimal quantityDelta,
        string? fromType = null,
        Guid? fromId = null,
        string? toType = null,
        Guid? toId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/sync/inventory_movements")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                product_id = productId,
                movement_type = movementType,
                from_location_type = fromType,
                from_location_id = fromId,
                to_location_type = toType,
                to_location_id = toId,
                quantity_delta = quantityDelta,
                idempotency_key = $"mv-{Guid.NewGuid():N}"[..32],
            }),
        };
        request.Headers.Add("Idempotency-Key", $"mv-{Guid.NewGuid():N}"[..32]);

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"{movementType} delta {quantityDelta} must apply: {await resp.Content.ReadAsStringAsync()}");
    }

    private static async Task<(Guid WarehouseId, Guid FieldInventoryId, Guid ProductId)> SeedLocationsAndProductAsync(
        PgTestScope scope, Guid doctorId)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var now = DateTimeOffset.UtcNow;
        var warehouseId = Guid.CreateVersion7();
        var fieldInventoryId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central",
            CreatedAt = now, UpdatedAt = now,
        });
        db.FieldInventories.Add(new FieldInventory
        {
            Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctorId,
            CreatedAt = now, UpdatedAt = now,
        });
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "منتج خاصية",
            Category = ProductCategory.Medication, PurchasePrice = 1m, SellingPrice = 2m,
            CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, fieldInventoryId, productId);
    }
}
