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
/// M4 task 19 — end-to-end: receive into the warehouse, load to a field inventory, the field
/// doctor sells from their own field stock (via /sync), then unload the remainder back. Quantities
/// must round-trip exactly across both locations: warehouse = received − loaded + unloaded, field
/// ends empty, and total on hand = received − sold.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LoadUnloadRoundTripTests
{
    [Fact]
    public async Task Load_FieldSale_Unload_RoundTripsQuantitiesAcrossLocations()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (doctor, warehouseId, fieldInventoryId, productId) = await SeedScenarioAsync(scope);

        await using var factory = new VetApiFactory();
        var jwtSvc = factory.Services.GetRequiredService<IJwtTokenService>();

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", jwtSvc.IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", jwtSvc.IssueAccessToken(new UserPrincipal(doctor.Id, doctor.EnvironmentId, RoleKey.VetField)).Token);

        // Receive 100 into the warehouse.
        await PostInventoryAsync(adminClient, "/inventory/receive", new
        {
            id = Guid.CreateVersion7(), productId, quantity = 100m, warehouseId,
            idempotencyKey = Key("recv"),
        });

        // Load 30 to the field inventory (warehouse → field).
        await PostInventoryAsync(adminClient, "/inventory/load-field", new
        {
            id = Guid.CreateVersion7(), productId, fieldInventoryId, quantity = 30m, warehouseId,
            idempotencyKey = Key("load"),
        });

        // Field doctor sells 12 from their own field inventory.
        await SaleDeductFromFieldAsync(doctorClient, productId, fieldInventoryId, 12m);

        // Unload the remaining 18 back to the warehouse (field → warehouse).
        await PostInventoryAsync(adminClient, "/inventory/unload-field", new
        {
            id = Guid.CreateVersion7(), productId, fieldInventoryId, quantity = 18m, warehouseId,
            idempotencyKey = Key("unload"),
        });

        await using var verify = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var warehouseQty = await StockAsync(verify, StockLocation.Warehouse, warehouseId, productId);
        var fieldQty = await StockAsync(verify, StockLocation.Field, fieldInventoryId, productId);

        warehouseQty.Should().Be(88m, "100 received − 30 loaded + 18 unloaded");
        fieldQty.Should().Be(0m, "30 loaded − 12 sold − 18 unloaded");
        (warehouseQty + fieldQty).Should().Be(88m, "total on hand = 100 received − 12 sold");
    }

    private static async Task PostInventoryAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", Key("hdr"));

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"POST {path} must succeed: {await resp.Content.ReadAsStringAsync()}");
    }

    private static async Task SaleDeductFromFieldAsync(
        HttpClient client, Guid productId, Guid fieldInventoryId, decimal quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/sync/inventory_movements")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                product_id = productId,
                movement_type = MovementType.SaleDeduct,
                from_location_type = StockLocation.Field,
                from_location_id = fieldInventoryId,
                quantity_delta = quantity,
                idempotency_key = Key("sale"),
            }),
        };
        request.Headers.Add("Idempotency-Key", Key("hdr"));

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"field sale_deduct must succeed: {await resp.Content.ReadAsStringAsync()}");
    }

    private static async Task<decimal> StockAsync(
        VetSystem.Infrastructure.Persistence.ApplicationDbContext db,
        string locationType, Guid locationId, Guid productId)
        => await db.StockItems
            .AsNoTracking()
            .Where(s => s.LocationType == locationType && s.LocationId == locationId && s.ProductId == productId)
            .Select(s => (decimal?)s.Quantity)
            .FirstOrDefaultAsync() ?? 0m;

    private static string Key(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..32];

    private static async Task<(User Doctor, Guid WarehouseId, Guid FieldInventoryId, Guid ProductId)> SeedScenarioAsync(
        PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var now = DateTimeOffset.UtcNow;

        var vetFieldRoleId = await db.Roles
            .IgnoreQueryFilters()
            .Where(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField)
            .Select(r => r.Id)
            .FirstAsync();

        var doctor = new User
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            RoleId = vetFieldRoleId,
            FullName = "Field Doctor",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "$2a$12$test.hash.placeholder.for.roundtrip",
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(doctor);

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central",
            CreatedAt = now, UpdatedAt = now,
        });

        var fieldInventoryId = Guid.CreateVersion7();
        db.FieldInventories.Add(new FieldInventory
        {
            Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctor.Id,
            CreatedAt = now, UpdatedAt = now,
        });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "لقاح",
            Category = ProductCategory.Medication, PurchasePrice = 5m, SellingPrice = 9m,
            CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (doctor, warehouseId, fieldInventoryId, productId);
    }
}
