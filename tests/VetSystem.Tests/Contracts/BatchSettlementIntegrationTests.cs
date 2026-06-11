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

namespace VetSystem.Tests.Contracts;

/// <summary>
/// M24 — batch settlement (تصفية الدورة) end to end. The fixture sells one product (purchase 10,
/// catalog 25) on credit across two field invoices (qty 10 + 4 → totals 250 + 100, farm balance 350),
/// then settles at 20 with a 30 discount:
/// repricing delta = (20−25)×14 = −70 · settled total = 350 − 70 − 30 = 250 ·
/// entitlement = the supervision fee 20 (M28 — System A carves it from the clinic margin; the discount
/// shrinks the clinic's drug-profit slice, not the doctor's fixed fee).
/// Covers the document + ledger adjustments + batch close + entitlement recompute transaction, the
/// preview read-model, every settle guard, the idempotent replay, the settled-batch freeze
/// (invariant #11) on REST and /sync, and settlement-price precedence over the billed catalog price.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BatchSettlementIntegrationTests
{
    [Fact]
    public async Task Settle_RepricesDiscountsClosesAndRecomputes_InOneTransaction()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 4m, total: 100m);

        // Preview is the settle screen's read model: aggregation, ledger position, guard flags.
        var preview = await client.GetFromJsonAsync<JsonElement>($"/batches/{batchId}/settlement/preview");
        preview.GetProperty("originalTotal").GetDecimal().Should().Be(350m);
        preview.GetProperty("ledgerBalance").GetDecimal().Should().Be(350m);
        preview.GetProperty("alreadySettled").GetBoolean().Should().BeFalse();
        preview.GetProperty("entitlementFrozen").GetBoolean().Should().BeFalse();
        preview.GetProperty("invoices").EnumerateArray().Should().HaveCount(2);
        var product = preview.GetProperty("products").EnumerateArray().Single();
        product.GetProperty("quantity").GetDecimal().Should().Be(14m);
        product.GetProperty("weightedAveragePrice").GetDecimal().Should().Be(25m);
        product.GetProperty("unitPrices").EnumerateArray().Single().GetDecimal().Should().Be(25m);
        product.GetProperty("originalAmount").GetDecimal().Should().Be(350m);

        var settle = await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId, settledUnitPrice = 20m } },
            discountAmount = 30m,
            notes = "تصفية نهاية الدورة",
            idempotencyKey = $"settle-{batchId:N}",
        });
        settle.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var settlement = await db.BatchSettlements.AsNoTracking().SingleAsync(s => s.BatchId == batchId);
            settlement.RepricingDelta.Should().Be(-70m);
            settlement.DiscountAmount.Should().Be(30m);
            settlement.OriginalTotal.Should().Be(350m);
            settlement.SettledTotal.Should().Be(250m, "System A does not charge the fee to the farmer");
            settlement.SupervisionFee.Should().Be(20m, "M28 — the fee snapshot on the settled revenue");
            settlement.SettledBy.Should().Be(admin.Id);

            var line = await db.BatchSettlementLines.AsNoTracking().SingleAsync(l => l.SettlementId == settlement.Id);
            line.ProductId.Should().Be(productId);
            line.SettledUnitPrice.Should().Be(20m);
            line.OriginalQuantity.Should().Be(14m);
            line.OriginalAmount.Should().Be(350m);
            line.Delta.Should().Be(-70m);

            // The money: two adjustment rows on the FARM ledger with the deterministic keys.
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.FarmId == farmId);
            ledger.Balance.Should().Be(250m, "350 − 70 reprice − 30 discount");
            ledger.Status.Should().Be(LedgerStatus.HasDebt);

            var adjustments = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.LedgerId == ledger.Id && e.EntryType == LedgerEntryType.Adjustment)
                .ToListAsync();
            adjustments.Should().HaveCount(2);
            adjustments.Single(e => e.IdempotencyKey == $"settle-reprice-{settlement.Id:N}").Amount.Should().Be(-70m);
            adjustments.Single(e => e.IdempotencyKey == $"settle-discount-{settlement.Id:N}").Amount.Should().Be(-30m);

            // The cycle closed and the entitlement is computed on the settled numbers (M30 — at settle).
            (await db.Batches.AsNoTracking().SingleAsync(b => b.Id == batchId)).Status.Should().Be(BatchStatus.Closed);
            var entitlement = await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId);
            entitlement.ComputedAmount.Should().Be(20m, "M28 — the entitlement is the supervision fee in full");
        }

        // The settlement read endpoint and the refreshed preview agree.
        var doc = await client.GetFromJsonAsync<JsonElement>($"/batches/{batchId}/settlement");
        doc.GetProperty("repricingDelta").GetDecimal().Should().Be(-70m);
        doc.GetProperty("settledTotal").GetDecimal().Should().Be(250m);
        doc.GetProperty("lines").EnumerateArray().Should().ContainSingle();

        var after = await client.GetFromJsonAsync<JsonElement>($"/batches/{batchId}/settlement/preview");
        after.GetProperty("alreadySettled").GetBoolean().Should().BeTrue();
        after.GetProperty("batchStatus").GetString().Should().Be(BatchStatus.Closed);

        // The batches list surfaces settledAt for the web's routing.
        var list = await client.GetFromJsonAsync<JsonElement>($"/batches?customerId={customerId}");
        list.EnumerateArray().Single().GetProperty("settledAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Settle_SystemB_ChargesSupervisionFeeOnTop_AfterRepriceAndDiscount()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id, entitlementSystem: "direct_fee");
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);

        // Settle at 20 (reprice −50) with a 10 discount. System B adds the supervision fee 20 on top.
        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId, settledUnitPrice = 20m } },
            discountAmount = 10m,
            idempotencyKey = $"settle-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var settlement = await db.BatchSettlements.AsNoTracking().SingleAsync(s => s.BatchId == batchId);
        settlement.SupervisionFee.Should().Be(20m);
        settlement.SettledTotal.Should().Be(210m, "250 − 50 reprice − 10 discount + 20 supervision fee");

        // Three adjustments on the farm ledger; the fee is a separate +20 with the deterministic key.
        var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.FarmId == farmId);
        ledger.Balance.Should().Be(210m, "250 − 50 − 10 + 20");
        var adjustments = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.LedgerId == ledger.Id && e.EntryType == LedgerEntryType.Adjustment)
            .ToListAsync();
        adjustments.Should().HaveCount(3);
        adjustments.Single(e => e.IdempotencyKey == $"settle-reprice-{settlement.Id:N}").Amount.Should().Be(-50m);
        adjustments.Single(e => e.IdempotencyKey == $"settle-discount-{settlement.Id:N}").Amount.Should().Be(-10m);
        adjustments.Single(e => e.IdempotencyKey == $"settle-supervision-{settlement.Id:N}").Amount.Should().Be(20m);

        // The doctor's entitlement is the fee credited in full.
        var entitlement = await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId);
        entitlement.CalculationSystem.Should().Be(EntitlementSystem.DirectFee);
        entitlement.ComputedAmount.Should().Be(20m);
    }

    [Fact]
    public async Task Settle_Guards_AlreadySettled_UnknownProduct_NoBasis()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);

        // A product with no effective lines on the batch → the client's preview drifted.
        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId = Guid.CreateVersion7(), settledUnitPrice = 9m } },
            discountAmount = 0m,
            idempotencyKey = $"settle-unknown-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // First real settle succeeds; a second (different keys) hits the one-settlement-per-batch wall.
        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 0m,
            idempotencyKey = $"settle-a-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 0m,
            idempotencyKey = $"settle-b-{batchId:N}",
        });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("batch_already_settled");

        // A batch with no effective invoices cannot carry a discount (nothing to discount against)…
        var (_, _, emptyBatch) = await SeedFarmBatchAsync(client, admin.Id);
        var noBasis = await PostAsync(client, $"/batches/{emptyBatch}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 10m,
            idempotencyKey = $"settle-nobasis-{emptyBatch:N}",
        });
        noBasis.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await noBasis.Content.ReadAsStringAsync()).Should().Contain("settlement_no_basis");

        // …but settling it bare is the plain "close the cycle" path.
        (await PostAsync(client, $"/batches/{emptyBatch}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 0m,
            idempotencyKey = $"settle-bare-{emptyBatch:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.Batches.AsNoTracking().SingleAsync(b => b.Id == emptyBatch)).Status.Should().Be(BatchStatus.Closed);
        var bare = await db.BatchSettlements.AsNoTracking().SingleAsync(s => s.BatchId == emptyBatch);
        bare.SettledTotal.Should().Be(0m);
        (await db.LedgerEntries.AsNoTracking().CountAsync(
            e => e.IdempotencyKey.StartsWith($"settle-reprice-") || e.IdempotencyKey.StartsWith("settle-discount-")))
            .Should().Be(0, "a bare settle posts no adjustments");
    }

    [Fact]
    public async Task Settle_Guards_ClosedLedger()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);

        // Pay in full, then close the farm account (M30 — closing no longer computes entitlements).
        (await PostAsync(client, "/receipt-vouchers", new
        {
            id = Guid.CreateVersion7(), customerId, farmId, amount = 250m, method = "cash",
            idempotencyKey = $"rv-{farmId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/farms/{farmId}/close-account", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A closed owner ledger blocks the settle outright (re-open it first to settle).
        var onClosed = await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId, settledUnitPrice = 20m } },
            discountAmount = 0m,
            idempotencyKey = $"settle-closed-{batchId:N}",
        });
        onClosed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await onClosed.Content.ReadAsStringAsync()).Should().Contain("owner_ledger_closed");
    }

    [Fact]
    public async Task Settle_ReplaysIdempotently_OnTheBodyKey()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);

        var body = new
        {
            lines = new[] { new { productId, settledUnitPrice = 20m } },
            discountAmount = 10m,
            idempotencyKey = $"settle-replay-{batchId:N}",
        };
        (await PostAsync(client, $"/batches/{batchId}/settle", body)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Same body key, fresh header key — must replay, not double-post.
        (await PostAsync(client, $"/batches/{batchId}/settle", body)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.BatchSettlements.AsNoTracking().CountAsync(s => s.BatchId == batchId)).Should().Be(1);
        var ledgerId = await db.Ledgers.AsNoTracking().Where(l => l.FarmId == farmId).Select(l => l.Id).SingleAsync();
        (await db.LedgerEntries.AsNoTracking()
            .CountAsync(e => e.LedgerId == ledgerId && e.EntryType == LedgerEntryType.Adjustment)).Should().Be(2);
    }

    [Fact]
    public async Task SettledBatch_FreezesItsInvoices_OnRestAndSync()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);
        var invoiceId = await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);

        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId, settledUnitPrice = 20m } },
            discountAmount = 0m,
            idempotencyKey = $"settle-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // REST: a new field invoice on the settled batch is rejected…
        var newInvoice = await IssueFieldInvoiceResponseAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 1m, total: 25m);
        newInvoice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await newInvoice.Content.ReadAsStringAsync()).Should().Contain("batch_settled");

        // …voiding a pre-settlement invoice is rejected…
        var voidResponse = await PostAsync(client, $"/invoices/{invoiceId}/void", null);
        voidResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await voidResponse.Content.ReadAsStringAsync()).Should().Contain("batch_settled");

        // …and the device write path (PUT /sync/invoices) hits the same wall.
        var syncPut = new HttpRequestMessage(HttpMethod.Put, "/sync/invoices")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["id"] = Guid.CreateVersion7(),
                ["invoice_type"] = "field",
                ["customer_id"] = customerId,
                ["batch_id"] = batchId,
                ["issued_by"] = admin.Id,
                ["subtotal"] = 25m,
                ["total"] = 25m,
                ["idempotency_key"] = $"sync-late-{batchId:N}",
            }),
        };
        syncPut.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        var syncResponse = await client.SendAsync(syncPut);
        syncResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await syncResponse.Content.ReadAsStringAsync()).Should().Contain("batch_settled");
    }

    [Fact]
    public async Task SettledPrice_Overrides_BilledPrice_InTheLedgerAndProfit()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var productId = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var (customerId, farmId, batchId) = await SeedFarmBatchAsync(client, admin.Id);

        // The field invoice bills at the catalog price (M29 — no contract pricing): 25 × 10 = 250 on credit.
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, farmId, batchId, productId, quantity: 10m, total: 250m);

        // Settle at 18 — below the catalog 25. M28: the entitlement is the fixed fee 20 regardless of
        // price, but the settled price supersedes the billed price in the LEDGER and drug profit.
        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = new[] { new { productId, settledUnitPrice = 18m } },
            discountAmount = 0m,
            idempotencyKey = $"settle-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var entitlement = await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId);
        entitlement.ComputedAmount.Should().Be(20m, "the entitlement is the supervision fee in full");

        // The ledger moved by the billed-vs-settled delta: (18−25)×10 = −70 → balance 180 (the settled
        // price beats the catalog 25, which was what the invoice billed).
        (await db.Ledgers.AsNoTracking().Where(l => l.FarmId == farmId).Select(l => l.Balance).SingleAsync())
            .Should().Be(180m);
    }

    // ---- helpers (the FarmLedgerSettlementTests harness) ----

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

    private static async Task<(Guid CustomerId, Guid FarmId, Guid BatchId)> SeedFarmBatchAsync(
        HttpClient client, Guid doctorId, string entitlementSystem = "drug_profit")
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "poultry_farm",
            fullName = "Settlement Farm Co",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var farmId = Guid.CreateVersion7();
        (await PostAsync(client, "/farms", new
        {
            id = farmId, customerId, name = $"Farm {farmId:N}"[..16], kind = "poultry",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

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
            entitlementSystem,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        return (customerId, farmId, batchId);
    }

    private static async Task<Guid> IssueFieldInvoiceAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid farmId, Guid batchId, Guid productId,
        decimal quantity, decimal total)
    {
        var response = await IssueFieldInvoiceResponseAsync(client, customerId, doctorId, farmId, batchId, productId, quantity, total);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> IssueFieldInvoiceResponseAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid farmId, Guid batchId, Guid productId,
        decimal quantity, decimal total)
    {
        var visitId = Guid.CreateVersion7();
        var visit = await PostAsync(client, "/visits", new
        {
            id = visitId, visitType = "field", customerId, farmId, doctorId, status = "in_progress",
        });
        visit.StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        return await PostAsync(client, $"/visits/{visitId}/field-invoice", new
        {
            id = invoiceId,
            batchId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity, discountAmount = 0m } },
            payments = new[] { new { method = "credit", amount = total } },
            idempotencyKey = $"fieldinv-{invoiceId:N}",
        });
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<Guid> SeedFieldStockAsync(PgTestScope scope, Guid doctorId)
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
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء التصفية",
            Category = ProductCategory.Medication, PurchasePrice = 10m, SellingPrice = 25m,
            CreatedAt = now, UpdatedAt = now,
        });

        db.StockItems.Add(new StockItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId,
            LocationType = StockLocation.Field, LocationId = fieldInventoryId, ProductId = productId,
            Quantity = 100m, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return productId;
    }
}
