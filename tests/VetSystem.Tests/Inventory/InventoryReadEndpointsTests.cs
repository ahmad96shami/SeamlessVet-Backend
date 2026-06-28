using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// BACKEND_PREREQS §2 — the web inventory reads (<c>GET /inventory/stock</c>,
/// <c>/inventory/movements</c>, <c>/inventory/field-inventories</c>). Authenticated, env-scoped,
/// offset-paged. Asserts the filters, the newest-first movement order, the field-inventory picker
/// projection, and that <c>belowReorderPoint</c>/<c>lowStockOnly</c> use the scan threshold
/// (reorder point inflated by <c>low_stock_threshold_pct</c>), not a literal reorder-point compare.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InventoryReadEndpointsTests
{
    private sealed record StockRow(
        Guid ProductId, string NameAr, string? NameLatin, string? Barcode,
        string Category, string? UnitOfMeasure, string LocationType, Guid LocationId,
        decimal Quantity, decimal ReorderPoint, string? ExpirationDate,
        decimal PurchasePrice, decimal SellingPrice, bool BelowReorderPoint);

    private sealed record MovementRow(
        Guid Id, Guid ProductId, string MovementType,
        string? FromLocationType, Guid? FromLocationId, string? ToLocationType, Guid? ToLocationId,
        decimal QuantityDelta, string? Reason, Guid? VisitId, Guid? InvoiceId,
        Guid PerformedBy, DateTimeOffset CreatedAt);

    private sealed record FieldInvRow(Guid Id, Guid DoctorId, string DoctorName);

    [Fact]
    public async Task Stock_ListsOnHand_FiltersByLocationAndSearch_AndFlagsLowStockViaThreshold()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var seed = await SeedAsync(scope, admin);
        await using var factory = new VetApiFactory();
        using var client = AuthorizedClient(factory, admin);

        // All on-hand rows: amox@wh, amox@field, enro@wh, expiring@wh.
        var all = await GetListAsync<StockRow>(client, "/inventory/stock");
        all.Should().HaveCount(4);

        // belowReorderPoint uses reorder × (1 + 20%): enro qty 11 ≤ 10×1.2 = 12 → flagged,
        // even though 11 > 10 (a literal reorder compare would NOT flag it).
        all.Single(r => r.ProductId == seed.EnroId && r.LocationType == StockLocation.Warehouse)
            .BelowReorderPoint.Should().BeTrue();
        var amoxWh = all.Single(r => r.ProductId == seed.AmoxId && r.LocationType == StockLocation.Warehouse);
        amoxWh.BelowReorderPoint.Should().BeFalse();
        // Enriched from the joined product (the design's stock table shows these columns).
        amoxWh.Category.Should().Be(ProductCategory.Medication);
        amoxWh.SellingPrice.Should().Be(9m);
        amoxWh.UnitOfMeasure.Should().BeNull();

        // locationType filter → field only.
        var field = await GetListAsync<StockRow>(client, "/inventory/stock?locationType=field");
        field.Should().ContainSingle()
            .Which.Should().Match<StockRow>(r => r.ProductId == seed.AmoxId && r.LocationId == seed.FieldInventoryId);

        // search hits the Latin name + barcode (case-insensitive).
        var byLatin = await GetListAsync<StockRow>(client, "/inventory/stock?search=amoxi");
        byLatin.Should().OnlyContain(r => r.ProductId == seed.AmoxId);
        byLatin.Should().HaveCount(2); // warehouse + field

        // lowStockOnly → only the enro warehouse row.
        var low = await GetListAsync<StockRow>(client, "/inventory/stock?lowStockOnly=true");
        low.Should().ContainSingle().Which.ProductId.Should().Be(seed.EnroId);

        // invalid filter → 409 invalid_location_type.
        var bad = await client.GetAsync("/inventory/stock?locationType=bogus");
        bad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await bad.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("invalid_location_type");
    }

    [Fact]
    public async Task Movements_NewestFirst_FilterByProductTypeLocation_AndFieldInventoriesPicker()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var seed = await SeedAsync(scope, admin);
        await using var factory = new VetApiFactory();
        using var client = AuthorizedClient(factory, admin);

        // All three movements, newest first (load is newest, then adjust, then receive).
        var all = await GetListAsync<MovementRow>(client, "/inventory/movements");
        all.Select(m => m.MovementType).Should()
            .ContainInOrder(MovementType.LoadToField, MovementType.Adjust, MovementType.Receive);

        // by movementType.
        var loads = await GetListAsync<MovementRow>(client, "/inventory/movements?movementType=load_to_field");
        loads.Should().ContainSingle().Which.ProductId.Should().Be(seed.AmoxId);

        // by productId.
        var enroMoves = await GetListAsync<MovementRow>(client, $"/inventory/movements?productId={seed.EnroId}");
        enroMoves.Should().ContainSingle().Which.MovementType.Should().Be(MovementType.Adjust);

        // by location — the field leg matches either side.
        var fieldMoves = await GetListAsync<MovementRow>(client, "/inventory/movements?locationType=field");
        fieldMoves.Should().ContainSingle().Which.MovementType.Should().Be(MovementType.LoadToField);

        // invalid movementType → 409.
        var bad = await client.GetAsync("/inventory/movements?movementType=teleport");
        bad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await bad.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("invalid_movement_type");

        // field-inventories picker resolves the doctor's name.
        var fieldInvs = await GetListAsync<FieldInvRow>(client, "/inventory/field-inventories");
        fieldInvs.Should().ContainSingle().Which.Should().Match<FieldInvRow>(f =>
            f.Id == seed.FieldInventoryId && f.DoctorId == seed.DoctorId && f.DoctorName == "د. ميدان");
    }

    [Fact]
    public async Task Stock_IncludeZeroStock_ListsUnstockedSellableProducts_AndExcludesConsumables()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var envId = scope.EnvironmentId;
        var now = DateTimeOffset.UtcNow;

        var stockedId = Guid.CreateVersion7();
        var unstockedId = Guid.CreateVersion7();
        var consumableId = Guid.CreateVersion7();
        var warehouseId = Guid.CreateVersion7();

        await using (var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = envId }))
        {
            db.Products.AddRange(
                new Product
                {
                    Id = stockedId, EnvironmentId = envId, NameAr = "أ-مخزون", Category = ProductCategory.Product,
                    PurchasePrice = 2m, SellingPrice = 5m, ReorderPoint = 0m, CreatedAt = now, UpdatedAt = now,
                },
                new Product
                {
                    Id = unstockedId, EnvironmentId = envId, NameAr = "ب-بدون-مخزون", Barcode = "ZERO-1",
                    Category = ProductCategory.Product, PurchasePrice = 3m, SellingPrice = 6m, ReorderPoint = 0m,
                    CreatedAt = now, UpdatedAt = now,
                },
                new Product
                {
                    Id = consumableId, EnvironmentId = envId, NameAr = "ج-مستهلك", Category = ProductCategory.Product,
                    IsConsumable = true, PurchasePrice = 1m, SellingPrice = 2m, ReorderPoint = 0m,
                    CreatedAt = now, UpdatedAt = now,
                });
            db.StockItems.Add(NewStock(envId, StockLocation.Warehouse, warehouseId, stockedId, 5m, now));
            await db.SaveChangesAsync();
        }

        await using var factory = new VetApiFactory();
        using var client = AuthorizedClient(factory, admin);

        // includeZeroStock: the unstocked sellable product appears (qty 0); the consumable never does.
        var withZero = await GetListAsync<StockRow>(client, "/inventory/stock?locationType=warehouse&includeZeroStock=true");
        withZero.Should().Contain(r => r.ProductId == stockedId && r.Quantity == 5m);
        withZero.Should().Contain(r => r.ProductId == unstockedId && r.Quantity == 0m);
        withZero.Should().NotContain(r => r.ProductId == consumableId);

        // Default (stocked rows only): the unstocked product is absent.
        var stockedOnly = await GetListAsync<StockRow>(client, "/inventory/stock?locationType=warehouse");
        stockedOnly.Should().Contain(r => r.ProductId == stockedId);
        stockedOnly.Should().NotContain(r => r.ProductId == unstockedId);
    }

    // ---- helpers ----

    private sealed record Seed(
        Guid WarehouseId, Guid FieldInventoryId, Guid DoctorId,
        Guid AmoxId, Guid EnroId, Guid ExpiringId);

    private static HttpClient AuthorizedClient(VetApiFactory factory, User admin)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<List<T>> GetListAsync<T>(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<List<T>>())!;
    }

    private static async Task<Seed> SeedAsync(PgTestScope scope, User admin)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var envId = scope.EnvironmentId;
        var now = DateTimeOffset.UtcNow;

        // 20% low-stock threshold (AdminTestSeed seeds 0) so the factor is exercised.
        var settings = await db.SystemSettings.FirstAsync();
        settings.LowStockThresholdPct = 20m;

        var vetFieldRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == envId && r.Key == RoleKey.VetField);

        var doctor = new User
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = envId,
            RoleId = vetFieldRole.Id,
            FullName = "د. ميدان",
            PhonePrimary = $"+97{Guid.NewGuid():N}"[..12],
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"F{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var warehouseId = Guid.CreateVersion7();
        var fieldInventoryId = Guid.CreateVersion7();
        var amoxId = Guid.CreateVersion7();
        var enroId = Guid.CreateVersion7();
        var expiringId = Guid.CreateVersion7();

        db.Users.Add(doctor);
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = envId, Name = "Central", CreatedAt = now, UpdatedAt = now });
        db.FieldInventories.Add(new FieldInventory { Id = fieldInventoryId, EnvironmentId = envId, DoctorId = doctor.Id, CreatedAt = now, UpdatedAt = now });

        db.Products.AddRange(
            new Product
            {
                Id = amoxId, EnvironmentId = envId, NameAr = "أموكسيسيلين", NameLatin = "Amoxicillin",
                Barcode = "AMOX-1", Category = ProductCategory.Medication, PurchasePrice = 5m, SellingPrice = 9m,
                ReorderPoint = 10m, CreatedAt = now, UpdatedAt = now,
            },
            new Product
            {
                Id = enroId, EnvironmentId = envId, NameAr = "إنروفلوكساسين", NameLatin = "Enrofloxacin",
                Barcode = "ENRO-1", Category = ProductCategory.Medication, PurchasePrice = 7m, SellingPrice = 12m,
                ReorderPoint = 10m, CreatedAt = now, UpdatedAt = now,
            },
            new Product
            {
                Id = expiringId, EnvironmentId = envId, NameAr = "لقاح", NameLatin = "Vaccine",
                Barcode = "VAC-1", Category = ProductCategory.Medication, PurchasePrice = 3m, SellingPrice = 6m,
                ReorderPoint = 5m, ExpirationDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(10),
                CreatedAt = now, UpdatedAt = now,
            });

        db.StockItems.AddRange(
            NewStock(envId, StockLocation.Warehouse, warehouseId, amoxId, 100m, now),
            NewStock(envId, StockLocation.Field, fieldInventoryId, amoxId, 20m, now),
            NewStock(envId, StockLocation.Warehouse, warehouseId, enroId, 11m, now), // 11 ≤ 10×1.2 → low
            NewStock(envId, StockLocation.Warehouse, warehouseId, expiringId, 50m, now));

        db.InventoryMovements.AddRange(
            NewMovement(envId, amoxId, MovementType.Receive, null, null, StockLocation.Warehouse, warehouseId, 100m, admin.Id, now.AddMinutes(-30)),
            NewMovement(envId, enroId, MovementType.Adjust, null, null, StockLocation.Warehouse, warehouseId, 11m, admin.Id, now.AddMinutes(-20)),
            NewMovement(envId, amoxId, MovementType.LoadToField, StockLocation.Warehouse, warehouseId, StockLocation.Field, fieldInventoryId, 20m, admin.Id, now.AddMinutes(-10)));

        await db.SaveChangesAsync();
        return new Seed(warehouseId, fieldInventoryId, doctor.Id, amoxId, enroId, expiringId);
    }

    private static StockItem NewStock(Guid envId, string locType, Guid locId, Guid productId, decimal qty, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId,
            LocationType = locType, LocationId = locId, ProductId = productId, Quantity = qty,
            CreatedAt = now, UpdatedAt = now,
        };

    private static InventoryMovement NewMovement(
        Guid envId, Guid productId, string type, string? fromType, Guid? fromId, string? toType, Guid? toId,
        decimal delta, Guid performedBy, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, ProductId = productId, MovementType = type,
            FromLocationType = fromType, FromLocationId = fromId, ToLocationType = toType, ToLocationId = toId,
            QuantityDelta = delta, PerformedBy = performedBy, IdempotencyKey = $"seed-{Guid.NewGuid():N}"[..32],
            CreatedAt = createdAt, UpdatedAt = createdAt,
        };
}
