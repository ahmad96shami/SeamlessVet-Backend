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
/// M12 task 18 — the inventory-movement report must reconcile to <c>Σ stock_items.quantity</c> per
/// (location, product) at any point in time (exit criterion). Drives the same round-trip as the M4
/// load/unload test (receive → load → field sale → unload) through the real endpoints, then asserts the
/// report's per-location inflows/outflows and that <c>inflows − outflows == balance == stock_items.quantity</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InventoryMovementReconciliationTests
{
    [Fact]
    public async Task InventoryMovement_ReconcilesToStockItems_PerLocation()
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

        await PostInventoryAsync(adminClient, "/inventory/receive", new
        {
            id = Guid.CreateVersion7(), productId, quantity = 100m, warehouseId, idempotencyKey = Key("recv"),
        });
        await PostInventoryAsync(adminClient, "/inventory/load-field", new
        {
            id = Guid.CreateVersion7(), productId, fieldInventoryId, quantity = 30m, warehouseId, idempotencyKey = Key("load"),
        });
        await SaleDeductFromFieldAsync(doctorClient, productId, fieldInventoryId, 12m);
        await PostInventoryAsync(adminClient, "/inventory/unload-field", new
        {
            id = Guid.CreateVersion7(), productId, fieldInventoryId, quantity = 18m, warehouseId, idempotencyKey = Key("unload"),
        });

        // All-time report for this product → both locations appear.
        var resp = await adminClient.GetAsync($"/reports/inventory-movement?productId={productId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await resp.Content.ReadFromJsonAsync<InventoryMovementReportResponse>())!;

        var warehouse = report.Rows.Single(r => r.LocationType == StockLocation.Warehouse && r.LocationId == warehouseId);
        var field = report.Rows.Single(r => r.LocationType == StockLocation.Field && r.LocationId == fieldInventoryId);

        // Warehouse: +100 received, −30 loaded out, +18 unloaded back → net 88.
        warehouse.Inflows.Should().Be(118m);
        warehouse.Outflows.Should().Be(30m);
        warehouse.NetChange.Should().Be(88m);
        warehouse.Balance.Should().Be(88m);

        // Field: +30 loaded in, −12 sold, −18 unloaded out → net 0.
        field.Inflows.Should().Be(30m);
        field.Outflows.Should().Be(30m);
        field.NetChange.Should().Be(0m);
        field.Balance.Should().Be(0m);

        // The reconciliation invariant + agreement with the materialized stock_items balances.
        await using var db = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true, EnvironmentId = scope.EnvironmentId, UserId = admin.Id,
        });
        foreach (var row in report.Rows)
        {
            (row.Inflows - row.Outflows).Should().Be(row.NetChange);
            row.NetChange.Should().Be(row.Balance, "all-time inflows − outflows must equal the on-hand balance");
            var stock = await StockAsync(db, row.LocationType, row.LocationId, row.ProductId);
            row.Balance.Should().Be(stock, "the report balance must equal the materialized stock_items quantity");
        }
    }

    // ---- helpers (mirror M4 LoadUnloadRoundTripTests) ----

    private static async Task PostInventoryAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Key("hdr"));
        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"POST {path}: {await resp.Content.ReadAsStringAsync()}");
    }

    private static async Task SaleDeductFromFieldAsync(HttpClient client, Guid productId, Guid fieldInventoryId, decimal quantity)
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
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"field sale_deduct: {await resp.Content.ReadAsStringAsync()}");
    }

    private static async Task<decimal> StockAsync(ApplicationDbContext db, string locationType, Guid locationId, Guid productId)
        => await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == locationType && s.LocationId == locationId && s.ProductId == productId)
            .Select(s => (decimal?)s.Quantity)
            .FirstOrDefaultAsync() ?? 0m;

    private static string Key(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..32];

    private static async Task<(User Doctor, Guid WarehouseId, Guid FieldInventoryId, Guid ProductId)> SeedScenarioAsync(
        PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var now = DateTimeOffset.UtcNow;

        var vetFieldRoleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField)
            .Select(r => r.Id).FirstAsync();

        var doctor = new User
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId, RoleId = vetFieldRoleId,
            FullName = "Field Doctor", PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "$2a$12$test.hash.placeholder.for.invmove", Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}", CreatedAt = now, UpdatedAt = now,
        };
        db.Users.Add(doctor);

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now });

        var fieldInventoryId = Guid.CreateVersion7();
        db.FieldInventories.Add(new FieldInventory { Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctor.Id, CreatedAt = now, UpdatedAt = now });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "لقاح",
            Category = ProductCategory.Medication, PurchasePrice = 5m, SellingPrice = 9m, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (doctor, warehouseId, fieldInventoryId, productId);
    }
}
