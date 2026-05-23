using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M4 task 17 — a <c>sale_deduct</c> beyond available stock is rejected end-to-end (HTTP 409
/// <c>negative_stock</c>) and a <see cref="NegativeStockAttemptedEvent"/> is published first. The
/// in-process API host has its <see cref="IDomainEventPublisher"/> swapped for a capturing double
/// so the event is observable, and the rejected write must leave <c>stock_items</c> untouched.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InventoryNegativeStockTests
{
    [Fact]
    public async Task SaleDeduct_BeyondAvailableStock_IsRejected_AndPublishesNegativeStockEvent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndProductAsync(scope);

        var capturing = new CapturingDomainEventPublisher();
        await using var factory = new VetApiFactory();
        await using var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDomainEventPublisher>();
                services.AddSingleton<IDomainEventPublisher>(capturing);
            }));
        using var client = configured.CreateClient();

        var jwt = configured.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        // Receive 3 units into the warehouse.
        var receive = await ReceiveAsync(client, productId, warehouseId, 3m);
        receive.StatusCode.Should().Be(HttpStatusCode.OK, await receive.Content.ReadAsStringAsync());

        // Attempt to sell 5 from the warehouse → would drive stock to -2.
        var resp = await SaleDeductAsync(client, productId, warehouseId, 5m);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, await resp.Content.ReadAsStringAsync());
        var error = await resp.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("code").GetString().Should().Be("negative_stock");

        capturing.Events.OfType<NegativeStockAttemptedEvent>().Should().ContainSingle()
            .Which.Should().Match<NegativeStockAttemptedEvent>(e =>
                e.ProductId == productId
                && e.LocationId == warehouseId
                && e.LocationType == StockLocation.Warehouse
                && e.AttemptedDelta == -5m
                && e.CurrentQuantity == 3m
                && e.PerformedBy == admin.Id);

        await using var verify = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var quantity = await verify.StockItems
            .AsNoTracking()
            .Where(si => si.ProductId == productId && si.LocationId == warehouseId)
            .Select(si => si.Quantity)
            .FirstAsync();

        quantity.Should().Be(3m, "the rejected deduction must not change stock");
    }

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseAndProductAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var now = DateTimeOffset.UtcNow;
        var warehouseId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            EnvironmentId = scope.EnvironmentId,
            Name = "Central",
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.Products.Add(new Product
        {
            Id = productId,
            EnvironmentId = scope.EnvironmentId,
            NameAr = "دواء اختبار",
            Category = ProductCategory.Medication,
            PurchasePrice = 1m,
            SellingPrice = 2m,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }

    private static async Task<HttpResponseMessage> ReceiveAsync(
        HttpClient client, Guid productId, Guid warehouseId, decimal quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/inventory/receive")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                productId,
                quantity,
                warehouseId,
                idempotencyKey = $"recv-{Guid.NewGuid():N}"[..32],
            }),
        };
        request.Headers.Add("Idempotency-Key", $"recv-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SaleDeductAsync(
        HttpClient client, Guid productId, Guid warehouseId, decimal quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/sync/inventory_movements")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                product_id = productId,
                movement_type = MovementType.SaleDeduct,
                from_location_type = StockLocation.Warehouse,
                from_location_id = warehouseId,
                quantity_delta = quantity,
                idempotency_key = $"sale-{Guid.NewGuid():N}"[..32],
            }),
        };
        request.Headers.Add("Idempotency-Key", $"sale-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }
}
