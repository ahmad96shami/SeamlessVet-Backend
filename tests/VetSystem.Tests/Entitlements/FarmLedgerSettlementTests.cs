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

namespace VetSystem.Tests.Entitlements;

/// <summary>
/// M16 — per-farm ledgers + settlement at farm granularity. Verifies charge routing (a farm-scoped
/// charge posts to the farm ledger, a clinic charge to the customer's own ledger), the customer's
/// aggregate balance (own + Σ farms), and that each <b>farm</b> ledger closes independently: closing one
/// farm never closes the other or the owning customer. (M30 — the settlement lock is gone; closing a
/// ledger no longer gates entitlements.)
/// </summary>
[Trait("Category", "Integration")]
public sealed class FarmLedgerSettlementTests
{
    [Fact]
    public async Task FarmScopedChargeRoutesToFarmLedger_ClinicChargeToCustomer_AndAggregates()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var farmId = await CreateFarmAsync(client, customerId);

        // Farm-scoped exam fee (100, unpaid) → posts to the farm ledger.
        await IssueExamFeeAsync(client, customerId, admin.Id, farmId, amount: 100m);
        // Clinic exam fee (60, unpaid, no farm) → posts to the customer's own ledger.
        await IssueExamFeeAsync(client, customerId, admin.Id, farmId: null, amount: 60m);

        await using (var db = NewContext(scope, admin.Id))
        {
            var ownBalance = await db.Ledgers.AsNoTracking()
                .Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync();
            var farmBalance = await db.Ledgers.AsNoTracking()
                .Where(l => l.FarmId == farmId).Select(l => l.Balance).SingleAsync();

            ownBalance.Should().Be(60m, "the no-farm charge posts to the customer's own ledger");
            farmBalance.Should().Be(100m, "the farm-scoped charge posts to the farm ledger");
        }

        // GET /customers/{id} aggregates own + farms and carries the per-farm breakdown.
        var detail = await client.GetFromJsonAsync<JsonElement>($"/customers/{customerId}");
        detail.GetProperty("balance").GetDecimal().Should().Be(160m);
        detail.GetProperty("ownBalance").GetDecimal().Should().Be(60m);
        detail.GetProperty("ledgerStatus").GetString().Should().Be(LedgerStatus.HasDebt);

        var farms = detail.GetProperty("farmLedgers").EnumerateArray().ToList();
        farms.Should().ContainSingle();
        farms[0].GetProperty("farmId").GetGuid().Should().Be(farmId);
        farms[0].GetProperty("balance").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task FarmStatement_ReturnsTheFarmLedgerEntries()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var farmId = await CreateFarmAsync(client, customerId);
        await IssueExamFeeAsync(client, customerId, admin.Id, farmId, amount: 100m);

        var statement = await client.GetFromJsonAsync<JsonElement>($"/farms/{farmId}/statement");
        statement.GetProperty("farmId").GetGuid().Should().Be(farmId);
        statement.GetProperty("customerId").GetGuid().Should().Be(customerId);
        statement.GetProperty("closingBalance").GetDecimal().Should().Be(100m);
        statement.GetProperty("entries").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task ClosingFarmA_ClosesOnlyFarmA_LeavesFarmBOwing_AndCustomerOpen()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id, quantity: 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var farmA = await CreateFarmAsync(client, customerId);
        var farmB = await CreateFarmAsync(client, customerId);

        var batchA = await CreateBatchAsync(client, customerId, admin.Id, farmA);
        var batchB = await CreateBatchAsync(client, customerId, admin.Id, farmB);

        // A field sale on each farm, on credit → each farm ledger carries 250.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmA, batchA, productId, quantity: 10m);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmB, batchB, productId, quantity: 10m);

        // Pay farm A in full (a farm-scoped receipt voucher), then close farm A's ledger.
        (await PostAsync(client, "/receipt-vouchers", new
        {
            id = Guid.CreateVersion7(),
            customerId,
            farmId = farmA,
            amount = 250m,
            method = "cash",
            idempotencyKey = $"rv-{farmA:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/farms/{farmA}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Farm B cannot be closed with an outstanding balance (the zero-balance gate survives M30).
        (await PostAsync(client, $"/farms/{farmB}/close-account", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "farm B still owes 250");

        await using (var verify = NewContext(scope, admin.Id))
        {
            // Farm A closed, farm B still owes — the customer stays open.
            (await verify.Ledgers.AsNoTracking().Where(l => l.FarmId == farmA).Select(l => l.Status).SingleAsync())
                .Should().Be(LedgerStatus.Closed);
            (await verify.Ledgers.AsNoTracking().Where(l => l.FarmId == farmB).Select(l => l.Status).SingleAsync())
                .Should().Be(LedgerStatus.HasDebt);
        }

        // Aggregate balance still reflects farm B's debt.
        var detail = await client.GetFromJsonAsync<JsonElement>($"/customers/{customerId}");
        detail.GetProperty("balance").GetDecimal().Should().Be(250m);
        detail.GetProperty("ledgerStatus").GetString().Should().Be(LedgerStatus.HasDebt);
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

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "poultry_farm",
            fullName = "Farm Ledger Co",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client, Guid customerId)
    {
        var farmId = Guid.CreateVersion7();
        (await PostAsync(client, "/farms", new
        {
            id = farmId,
            customerId,
            name = $"Farm {farmId:N}"[..16],
            kind = "poultry",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return farmId;
    }

    private static async Task<Guid> CreateBatchAsync(HttpClient client, Guid customerId, Guid doctorId, Guid farmId)
    {
        var batchId = Guid.CreateVersion7();
        (await PostAsync(client, "/batches", new
        {
            id = batchId,
            customerId,
            farmId,
            responsibleDoctorId = doctorId,
            animalCount = 1000,
            startDate = "2026-01-01",
            supervisionFeeModel = FeeModel.FixedAmount,
            supervisionFeeValue = 20m,
            entitlementEnabled = true,
            entitlementSystem = "drug_profit",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return batchId;
    }

    private static async Task IssueExamFeeAsync(HttpClient client, Guid customerId, Guid doctorId, Guid? farmId, decimal amount)
    {
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "field", customerId, farmId, doctorId, status = "in_progress",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/visits/{visitId}/exam-fee-invoice", new
        {
            id = Guid.CreateVersion7(),
            amount,
            payments = Array.Empty<object>(),
            idempotencyKey = $"exam-{visitId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task IssueFieldInvoiceAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid farmId, Guid batchId, Guid productId, decimal quantity)
    {
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "field", customerId, farmId, doctorId, status = "in_progress",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, $"/visits/{visitId}/field-invoice", new
        {
            id = invoiceId,
            batchId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity, discountAmount = 0m } },
            payments = new[] { new { method = "credit", amount = 250m } },
            idempotencyKey = $"fieldinv-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<(Guid FieldInventoryId, Guid ProductId)> SeedFieldStockAsync(
        PgTestScope scope, Guid doctorId, decimal quantity, decimal purchase, decimal selling)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var fieldInventoryId = Guid.CreateVersion7();
        db.FieldInventories.Add(new FieldInventory
        {
            Id = fieldInventoryId, EnvironmentId = scope.EnvironmentId, DoctorId = doctorId,
            CreatedAt = now, UpdatedAt = now,
        });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء",
            Category = ProductCategory.Medication, PurchasePrice = purchase, SellingPrice = selling,
            CreatedAt = now, UpdatedAt = now,
        });

        db.StockItems.Add(new StockItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId,
            LocationType = StockLocation.Field, LocationId = fieldInventoryId, ProductId = productId,
            Quantity = quantity, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (fieldInventoryId, productId);
    }
}
