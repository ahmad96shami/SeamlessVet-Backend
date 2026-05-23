using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Visits;

/// <summary>
/// M5 task 23 — an <c>administered_in_clinic</c> prescription posts its <c>sale_deduct</c>
/// inventory movement in the same transaction as the prescription row. The negative-stock case
/// proves atomicity: the whole write rolls back (no prescription, no stock change).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PrescriptionInventoryIntegrationTests
{
    [Fact]
    public async Task AdministeredPrescription_DeductsStock_AndRollsBackOnNegativeStock()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseStockAsync(scope, 100m);

        await using var factory = new VetApiFactory();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", jwt.IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Rx Owner", phonePrimary = "+970590001234",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // administered_in_clinic deducts 5 from the warehouse, tagged with the visit.
        (await PostAsync(client, "/prescriptions", new
        {
            id = Guid.CreateVersion7(), visitId, productId,
            dispenseType = DispenseType.AdministeredInClinic, quantity = 5m,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await StockAsync(scope, warehouseId, productId)).Should().Be(95m, "100 received − 5 administered");

        await using (var db = NewContext(scope, admin.Id))
        {
            var movement = await db.InventoryMovements.AsNoTracking().FirstOrDefaultAsync(
                m => m.VisitId == visitId && m.MovementType == MovementType.SaleDeduct);
            movement.Should().NotBeNull();
            movement!.QuantityDelta.Should().Be(-5m);
        }

        // A prescription that would drive stock negative must roll back entirely (atomic write).
        var rolledBackRxId = Guid.CreateVersion7();
        var neg = await PostAsync(client, "/prescriptions", new
        {
            id = rolledBackRxId, visitId, productId,
            dispenseType = DispenseType.AdministeredInClinic, quantity = 100_000m,
        });
        neg.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Prescriptions.AsNoTracking().AnyAsync(p => p.Id == rolledBackRxId))
                .Should().BeFalse("the failed administration rolled the prescription back");
        }

        (await StockAsync(scope, warehouseId, productId)).Should().Be(95m, "rollback left stock untouched");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<decimal> StockAsync(PgTestScope scope, Guid warehouseId, Guid productId)
    {
        await using var db = NewContext(scope, null);
        return await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == StockLocation.Warehouse && s.LocationId == warehouseId && s.ProductId == productId)
            .Select(s => (decimal?)s.Quantity)
            .FirstOrDefaultAsync() ?? 0m;
    }

    private static VetSystem.Infrastructure.Persistence.ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseStockAsync(PgTestScope scope, decimal quantity)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now,
        });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء",
            Category = ProductCategory.Medication, PurchasePrice = 3m, SellingPrice = 7m,
            CreatedAt = now, UpdatedAt = now,
        });

        db.StockItems.Add(new StockItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId,
            LocationType = StockLocation.Warehouse, LocationId = warehouseId, ProductId = productId,
            Quantity = quantity, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }
}
