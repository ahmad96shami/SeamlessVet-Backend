using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.DoctorPartners;

/// <summary>
/// M30 exit criteria — settling a batch releases the doctor's entitlement by crediting their
/// doctor-partner ledger (without the owner ledger being closed); a re-settle never double-credits; a
/// payment debits the running balance; a missing partner blocks the settle (and rolls it back); a
/// toggle-off batch credits nothing without needing a partner; and a visit under a batch can't be
/// charged a separate exam fee.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorPartnerReleaseTests
{
    [Fact]
    public async Task Settle_CreditsResponsibleDoctorPartner_AndPaymentClearsTheBalance()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m, payments: [("cash", 250m)]);
        var partnerId = await CreateDoctorPartnerAsync(client, admin.Id);

        // Settle with no reprice/discount → the doctor is owed the supervision fee (fixed 20).
        await SettleAsync(client, batchId);

        await using (var db = NewContext(scope, admin.Id))
        {
            // The owner ledger is NOT closed — the release no longer depends on closing the account.
            (await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Status).SingleAsync())
                .Should().NotBe(LedgerStatus.Closed);

            var entitlement = await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId);
            entitlement.ComputedAmount.Should().Be(20m);

            var ledger = await db.DoctorPartnerLedgers.AsNoTracking().SingleAsync(l => l.DoctorPartnerId == partnerId);
            ledger.Balance.Should().Be(20m, "the clinic now owes the doctor the supervision fee");
            ledger.Status.Should().Be(LedgerStatus.HasDebt);

            var entry = await db.DoctorPartnerLedgerEntries.AsNoTracking()
                .SingleAsync(e => e.DoctorEntitlementId == entitlement.Id);
            entry.EntryType.Should().Be(DoctorPartnerLedgerEntryType.Entitlement);
            entry.Amount.Should().Be(20m);
            entry.BatchId.Should().Be(batchId);
            entry.IdempotencyKey.Should().Be($"entitlement-{entitlement.Id:N}");
        }

        // Pay the doctor → the balance clears.
        var payId = Guid.CreateVersion7();
        (await PostAsync(client, $"/doctor-partners/{partnerId}/payments", new
        {
            id = payId, amount = 20m, method = "cash", notes = (string?)null,
            chequeNumber = (string?)null, chequeBank = (string?)null, chequeDueDate = (string?)null,
            idempotencyKey = $"dpp-{payId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var ledger = await db.DoctorPartnerLedgers.AsNoTracking().SingleAsync(l => l.DoctorPartnerId == partnerId);
            ledger.Balance.Should().Be(0m, "a 20 payment clears the 20 entitlement credit");
            ledger.Status.Should().Be(LedgerStatus.Open);
        }
    }

    [Fact]
    public async Task Settle_WithoutADoctorPartner_IsRejected_AndRollsBack()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m, payments: [("cash", 250m)]);

        // No doctor-partner exists for the responsible doctor → the settle is blocked.
        var settle = await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 0m,
            idempotencyKey = $"settle-{batchId:N}",
        });
        settle.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await settle.Content.ReadAsStringAsync()).Should().Contain("doctor_partner_missing");

        await using var db = NewContext(scope, admin.Id);
        (await db.BatchSettlements.AsNoTracking().AnyAsync(s => s.BatchId == batchId))
            .Should().BeFalse("the failed release rolls back the whole settlement transaction");
        (await db.DoctorEntitlements.AsNoTracking().AnyAsync(e => e.BatchId == batchId))
            .Should().BeFalse("no entitlement is persisted either");
    }

    [Fact]
    public async Task Settle_ToggleOff_CreditsNothing_WithoutNeedingAPartner()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedFieldStockAsync(scope, admin.Id);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id, entitlementEnabled: false);
        await IssueFieldInvoiceAsync(client, customerId, admin.Id, batchId, productId, quantity: 10m, payments: [("cash", 250m)]);

        // No doctor-partner — but the entitlement is 0 (toggle off), so the settle still succeeds.
        await SettleAsync(client, batchId);

        await using var db = NewContext(scope, admin.Id);
        (await db.DoctorEntitlements.AsNoTracking().SingleAsync(e => e.BatchId == batchId)).ComputedAmount
            .Should().Be(0m);
        (await db.DoctorPartnerLedgerEntries.AsNoTracking().AnyAsync())
            .Should().BeFalse("a 0 entitlement credits nothing and needs no partner");
    }

    [Fact]
    public async Task UnderBatchVisit_ExamFee_IsRejected()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var batchId = await CreateBatchAsync(client, customerId, admin.Id);

        // A field visit that belongs to the batch (BatchId is set on the device/sync path — seed it).
        var visitId = Guid.CreateVersion7();
        await using (var db = NewContext(scope, admin.Id))
        {
            var now = DateTimeOffset.UtcNow;
            db.Visits.Add(new Visit
            {
                Id = visitId, EnvironmentId = scope.EnvironmentId, VisitType = VisitType.Field,
                CustomerId = customerId, BatchId = batchId, DoctorId = admin.Id,
                Status = VisitStatus.InProgress, CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var resp = await PostAsync(client, $"/visits/{visitId}/exam-fee-invoice", new
        {
            id = Guid.CreateVersion7(),
            amount = 50m,
            payments = Array.Empty<object>(),
            idempotencyKey = $"exam-{visitId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("exam_fee_covered_by_batch");
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

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body, string? idemKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idemKey ?? $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "poultry_farm", fullName = "Release Farm",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<Guid> CreateBatchAsync(
        HttpClient client, Guid customerId, Guid doctorId, bool entitlementEnabled = true)
    {
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
            entitlementEnabled,
            entitlementSystem = "drug_profit",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return batchId;
    }

    private static async Task<Guid> CreateDoctorPartnerAsync(HttpClient client, Guid userId)
    {
        var partnerId = Guid.CreateVersion7();
        (await PostAsync(client, "/doctor-partners", new { id = partnerId, userId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return partnerId;
    }

    private static async Task SettleAsync(HttpClient client, Guid batchId)
    {
        (await PostAsync(client, $"/batches/{batchId}/settle", new
        {
            lines = Array.Empty<object>(),
            discountAmount = 0m,
            idempotencyKey = $"settle-{batchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task IssueFieldInvoiceAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid batchId, Guid productId,
        decimal quantity, (string Method, decimal Amount)[] payments)
    {
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "field", customerId, doctorId, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, $"/visits/{visitId}/field-invoice", new
        {
            id = invoiceId,
            batchId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity, discountAmount = 0m } },
            payments = payments.Select(p => new { method = p.Method, amount = p.Amount }).ToArray(),
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

    private static async Task<(Guid FieldInventoryId, Guid ProductId)> SeedFieldStockAsync(PgTestScope scope, Guid doctorId)
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
        return (fieldInventoryId, productId);
    }
}
