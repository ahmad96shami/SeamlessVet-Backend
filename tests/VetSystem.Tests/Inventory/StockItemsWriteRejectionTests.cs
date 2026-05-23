using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M4 exit criterion — a <c>/sync/stock_items</c> write with an absolute quantity is rejected.
/// stock_items is a server-derived materialized balance; clients change stock only by posting
/// signed deltas to <c>/sync/inventory_movements</c> (SCHEMA "Key invariants" #2). The handler
/// returns 405 (the method is not allowed on this resource).
/// </summary>
[Trait("Category", "Integration")]
public sealed class StockItemsWriteRejectionTests
{
    [Fact]
    public async Task PutStockItem_WithAbsoluteQuantity_IsRejectedWith405()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var request = new HttpRequestMessage(HttpMethod.Put, "/sync/stock_items")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                location_type = "warehouse",
                location_id = Guid.CreateVersion7(),
                product_id = Guid.CreateVersion7(),
                quantity = 999m, // absolute quantity — forbidden
            }),
        };
        request.Headers.Add("Idempotency-Key", $"stk-{Guid.NewGuid():N}"[..32]);

        var resp = await client.SendAsync(request);

        resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, await resp.Content.ReadAsStringAsync());
        var error = await resp.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("code").GetString().Should().Be("stock_items_server_managed");
    }
}
