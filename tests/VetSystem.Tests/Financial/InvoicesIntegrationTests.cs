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

namespace VetSystem.Tests.Financial;

/// <summary>
/// M7 tasks 16–18 + exit criteria — POS/void issuance against the API over a real Postgres env:
/// mixed-payment sum, cost_price snapshot, atomic ledger posting, idempotent replay, walk-in
/// (no ledger), void semantics, and visit-charge auto-assembly with inventory deduction.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InvoicesIntegrationTests
{
    [Fact]
    public async Task Pos_MixedPayment_SnapshotsCost_AndPostsLedgerForOutstandingPortion()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        var invoiceId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId,
            customerId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity = 3m, discountAmount = 0m } },
            payments = new[] { new { method = "cash", amount = 30m }, new { method = "credit", amount = 45m } },
            idempotencyKey = $"inv-{invoiceId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var invoice = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId);
        invoice.Total.Should().Be(75m, "3 × 25");
        invoice.Subtotal.Should().Be(75m);

        var item = await db.InvoiceItems.AsNoTracking().SingleAsync(it => it.InvoiceId == invoiceId);
        item.CostPrice.Should().Be(10m, "cost_price is snapshotted from products.purchase_price, never the selling price");
        item.UnitPrice.Should().Be(25m);
        item.LineTotal.Should().Be(75m);

        var paymentSum = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == invoiceId).SumAsync(p => p.Amount);
        paymentSum.Should().Be(75m, "mixed payments reconcile to the invoice total");

        var entry = await db.LedgerEntries.AsNoTracking().SingleAsync(e => e.InvoiceId == invoiceId);
        entry.EntryType.Should().Be(LedgerEntryType.Invoice);
        entry.Amount.Should().Be(45m, "ledger records the outstanding portion: 75 total − 30 non-credit payment");
    }

    [Fact]
    public async Task Pos_IdempotencyReplay_DoesNotDuplicate()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var body = new
        {
            id = Guid.CreateVersion7(),
            customerId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity = 2m, discountAmount = 0m } },
            payments = new[] { new { method = "cash", amount = 50m } },
            idempotencyKey = "stable-idem-key-001",
        };
        var key = $"hdr-{Guid.NewGuid():N}"[..32];

        (await PostAsync(client, "/pos/invoices", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/pos/invoices", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.Invoices.AsNoTracking().CountAsync(i => i.CustomerId == customerId))
            .Should().Be(1, "the same idempotency key collapses retries to one invoice");
    }

    [Fact]
    public async Task Pos_WalkIn_WritesNoLedgerEntry()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var invoiceId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId,
            customerId = (Guid?)null,
            discountAmount = 0m,
            items = new[] { new { productId, quantity = 2m, discountAmount = 0m } },
            payments = new[] { new { method = "cash", amount = 50m } },
            idempotencyKey = $"walkin-{invoiceId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId)).CustomerId.Should().BeNull();
        (await db.LedgerEntries.AsNoTracking().AnyAsync(e => e.InvoiceId == invoiceId))
            .Should().BeFalse("a walk-in sale has no customer, so it posts no ledger entry");
    }

    [Fact]
    public async Task Void_AppendsVoidRow_PlusCompensatingLedger_LeavingOriginalUntouched()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId,
            customerId,
            discountAmount = 0m,
            items = new[] { new { productId, quantity = 3m, discountAmount = 0m } },
            payments = new[] { new { method = "cash", amount = 30m }, new { method = "credit", amount = 45m } },
            idempotencyKey = $"void-src-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var voidResp = await PostAsync(client, $"/invoices/{invoiceId}/void", null);
        voidResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        (await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId)).Status
            .Should().Be(InvoiceStatus.Issued, "the original invoice row is never mutated (append-only)");

        var voidRow = await db.Invoices.AsNoTracking().SingleAsync(i => i.VoidOfInvoiceId == invoiceId);
        voidRow.Status.Should().Be(InvoiceStatus.Void);
        voidRow.Total.Should().Be(-75m, "the void row carries negated totals");

        var adjustment = await db.LedgerEntries.AsNoTracking().SingleAsync(e => e.InvoiceId == voidRow.Id);
        adjustment.EntryType.Should().Be(LedgerEntryType.Adjustment);
        adjustment.Amount.Should().Be(-45m, "the compensation reverses the original's posted 45");

        var balance = await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).FirstAsync();
        balance.Should().Be(0m, "issuing (+45) then voiding (−45) nets to zero");
    }

    [Fact]
    public async Task Pos_LinkedVisit_AutoAssemblesDispensedMedAndProcedure_AndDeductsStock()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        var serviceId = Guid.CreateVersion7();
        (await PostAsync(client, "/admin/services", new { id = serviceId, nameAr = "فحص", category = "exam", defaultPrice = 40m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/procedures", new { id = Guid.CreateVersion7(), visitId, serviceId, price = 40m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/prescriptions", new { id = Guid.CreateVersion7(), visitId, productId, dispenseType = "dispensed_to_owner", quantity = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId,
            customerId,
            visitId,
            discountAmount = 0m,
            items = Array.Empty<object>(),
            payments = Array.Empty<object>(),
            idempotencyKey = $"assemble-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var items = await db.InvoiceItems.AsNoTracking().Where(it => it.InvoiceId == invoiceId).ToListAsync();
        items.Should().HaveCount(2, "the dispensed med and the procedure auto-assemble");
        items.Should().ContainSingle(it => it.PrescriptionId != null && it.ProductId == productId && it.LineTotal == 50m);
        items.Should().ContainSingle(it => it.ProcedureId != null && it.ServiceId == serviceId && it.LineTotal == 40m);

        var stock = await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == StockLocation.Warehouse && s.LocationId == warehouseId && s.ProductId == productId)
            .Select(s => s.Quantity).FirstAsync();
        stock.Should().Be(98m, "the dispensed med (qty 2) is deducted at issuance");
    }

    [Fact]
    public async Task Pos_BackLinkedVisitLines_HonourPriceAndDiscount_ServerWinsQuantity_NoDoubleAssembly()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);

        var serviceId = Guid.CreateVersion7();
        (await PostAsync(client, "/admin/services", new { id = serviceId, nameAr = "فحص", category = "exam", defaultPrice = 40m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var procedureId = Guid.CreateVersion7();
        (await PostAsync(client, "/procedures", new { id = procedureId, visitId, serviceId, price = 40m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var prescriptionId = Guid.CreateVersion7();
        (await PostAsync(client, "/prescriptions", new { id = prescriptionId, visitId, productId, dispenseType = "dispensed_to_owner", quantity = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The POS cart sends the visit charges as explicit lines — price/discount adjusted at the
        // till, quantity tampered with (5) to prove the server wins — plus one ad-hoc extra item.
        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId,
            customerId,
            visitId,
            discountAmount = 0m,
            items = new object[]
            {
                new { productId, prescriptionId, quantity = 5m, unitPrice = 22m, discountAmount = 4m },
                new { serviceId, procedureId, quantity = 1m, unitPrice = 35m, discountAmount = 0m },
                new { productId, quantity = 1m, discountAmount = 0m },
            },
            payments = Array.Empty<object>(),
            idempotencyKey = $"backlink-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var items = await db.InvoiceItems.AsNoTracking().Where(it => it.InvoiceId == invoiceId).ToListAsync();
        items.Should().HaveCount(3, "back-linked charges must not auto-assemble a second time");

        var rxLine = items.Single(it => it.PrescriptionId == prescriptionId);
        rxLine.Quantity.Should().Be(2m, "quantity is server-authoritative (the prescription's), not the request's 5");
        rxLine.UnitPrice.Should().Be(22m, "the till's price adjustment is honoured");
        rxLine.DiscountAmount.Should().Be(4m);
        rxLine.LineTotal.Should().Be(40m, "2 × 22 − 4");

        var procedureLine = items.Single(it => it.ProcedureId == procedureId);
        procedureLine.Quantity.Should().Be(1m);
        procedureLine.UnitPrice.Should().Be(35m, "the till's price adjustment is honoured over the procedure price");

        items.Should().ContainSingle(it => it.PrescriptionId == null && it.ProcedureId == null,
            "the ad-hoc extra line rides alongside the visit charges");

        // A second invoice for the same visit assembles nothing — the charges are billed.
        var secondId = Guid.CreateVersion7();
        var second = await PostAsync(client, "/pos/invoices", new
        {
            id = secondId,
            customerId,
            visitId,
            discountAmount = 0m,
            items = Array.Empty<object>(),
            payments = Array.Empty<object>(),
            idempotencyKey = $"backlink2-{secondId:N}",
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "nothing left to bill on this visit");
    }

    [Fact]
    public async Task Pos_BackLinkedLine_AlreadyBilled_Conflicts()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var prescriptionId = Guid.CreateVersion7();
        (await PostAsync(client, "/prescriptions", new { id = prescriptionId, visitId, productId, dispenseType = "dispensed_to_owner", quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var firstId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = firstId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { productId, prescriptionId, quantity = 1m, discountAmount = 0m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"billed1-{firstId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var secondId = Guid.CreateVersion7();
        var second = await PostAsync(client, "/pos/invoices", new
        {
            id = secondId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { productId, prescriptionId, quantity = 1m, discountAmount = 0m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"billed2-{secondId:N}",
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "a prescription can be billed once");
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
        var resp = await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Fin Cust",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseStockAsync(
        PgTestScope scope, decimal quantity, decimal purchase, decimal selling)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now });

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
            LocationType = StockLocation.Warehouse, LocationId = warehouseId, ProductId = productId,
            Quantity = quantity, CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }
}
