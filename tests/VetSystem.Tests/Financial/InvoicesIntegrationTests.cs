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

    [Fact]
    public async Task BilledVisitCharges_CannotBeDeletedOrRepriced_OnTheVisit()
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
        (await PostAsync(client, "/prescriptions", new { id = prescriptionId, visitId, productId, dispenseType = "dispensed_to_owner", quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"guard-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Billed → the clinical rows backing the invoice lines are frozen.
        var rxDelete = await SendAsync(client, HttpMethod.Delete, $"/prescriptions/{prescriptionId}", null);
        rxDelete.StatusCode.Should().Be(HttpStatusCode.Conflict, "a billed prescription cannot be removed from the visit");
        (await rxDelete.Content.ReadAsStringAsync()).Should().Contain("prescription_billed");

        var procDelete = await SendAsync(client, HttpMethod.Delete, $"/procedures/{procedureId}", null);
        procDelete.StatusCode.Should().Be(HttpStatusCode.Conflict, "a billed procedure cannot be removed from the visit");
        (await procDelete.Content.ReadAsStringAsync()).Should().Contain("procedure_billed");

        var reprice = await SendAsync(client, HttpMethod.Patch, $"/procedures/{procedureId}", new { price = 99m });
        reprice.StatusCode.Should().Be(HttpStatusCode.Conflict, "a billed procedure cannot be re-priced");
        (await reprice.Content.ReadAsStringAsync()).Should().Contain("procedure_billed");

        // The clinical RESULT stays editable (round-tripping the unchanged price must not trip the guard).
        (await SendAsync(client, HttpMethod.Patch, $"/procedures/{procedureId}", new { price = 40m, resultText = "نتيجة" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Unbilled rows still delete freely.
        var freeRx = Guid.CreateVersion7();
        (await PostAsync(client, "/prescriptions", new { id = freeRx, visitId, productId, dispenseType = "dispensed_to_owner", quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, HttpMethod.Delete, $"/prescriptions/{freeRx}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "an unbilled prescription is still the vet's to remove");
    }

    [Fact]
    public async Task Pos_LinkedVisit_AutoAssemblesCatalogVaccination_Once_LegacyFreeTextNever()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var vaccineServiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/admin/services", new { id = vaccineServiceId, nameAr = "لقاح السعار", category = "vaccination", defaultPrice = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Catalog-linked, no explicit price → snapshots the catalog default (30).
        var vaccinationId = Guid.CreateVersion7();
        (await PostAsync(client, "/vaccinations", new
        {
            id = vaccinationId, customerId, visitId, serviceId = vaccineServiceId,
            vaccineType = "لقاح السعار", dateGiven = "2026-06-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Legacy free-text vaccination on the same visit — a clinical record only, never billed.
        (await PostAsync(client, "/vaccinations", new
        {
            id = Guid.CreateVersion7(), customerId, visitId,
            vaccineType = "لقاح قديم", dateGiven = "2026-06-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        var posResp = await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"vax-{invoiceId:N}",
        });
        posResp.StatusCode.Should().Be(HttpStatusCode.OK, await posResp.Content.ReadAsStringAsync());

        await using var db = NewContext(scope, admin.Id);
        var items = await db.InvoiceItems.AsNoTracking().Where(it => it.InvoiceId == invoiceId).ToListAsync();
        items.Should().HaveCount(1, "only the catalog-linked vaccination assembles; free-text never bills");
        var line = items.Single();
        line.VaccinationId.Should().Be(vaccinationId);
        line.ServiceId.Should().Be(vaccineServiceId);
        line.Quantity.Should().Be(1m);
        line.UnitPrice.Should().Be(30m, "the create snapshotted the catalog default price");
        line.LineTotal.Should().Be(30m);

        // A second invoice for the same visit has nothing left to bill.
        var secondId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = secondId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"vax2-{secondId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict, "the vaccination is billed; the free-text one never assembles");
    }

    [Fact]
    public async Task Pos_BackLinkedVaccinationLine_TillEditsPriceDiscount_ServerWinsQuantity_BilledOnce()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var vaccineServiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/admin/services", new { id = vaccineServiceId, nameAr = "لقاح", category = "vaccination", defaultPrice = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var otherServiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/admin/services", new { id = otherServiceId, nameAr = "خدمة أخرى", category = "exam", defaultPrice = 99m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var vaccinationId = Guid.CreateVersion7();
        (await PostAsync(client, "/vaccinations", new
        {
            id = vaccinationId, customerId, visitId, serviceId = vaccineServiceId, price = 18m,
            vaccineType = "لقاح", dateGiven = "2026-06-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The back-linked line must agree with the vaccination's catalog vaccine.
        var mismatchId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = mismatchId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { serviceId = otherServiceId, vaccinationId, quantity = 1m, discountAmount = 0m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"vaxmm-{mismatchId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict, "the line's service must match the vaccination's");

        // Till-edited price/discount are honoured; the tampered quantity (5) is not.
        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { serviceId = vaccineServiceId, vaccinationId, quantity = 5m, unitPrice = 15m, discountAmount = 2m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"vaxbl-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var line = await db.InvoiceItems.AsNoTracking().SingleAsync(it => it.InvoiceId == invoiceId);
        line.VaccinationId.Should().Be(vaccinationId, "the explicit back-linked line suppresses auto-assembly");
        line.Quantity.Should().Be(1m, "a vaccination always bills as a single line");
        line.UnitPrice.Should().Be(15m, "the till's price adjustment is honoured over the snapshot");
        line.DiscountAmount.Should().Be(2m);
        line.LineTotal.Should().Be(13m);

        var rebillId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = rebillId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { serviceId = vaccineServiceId, vaccinationId, quantity = 1m, discountAmount = 0m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"vaxrb-{rebillId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict, "a vaccination can be billed once");
    }

    [Fact]
    public async Task BilledVaccination_IsFrozen_OnTheVisit()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var vaccineServiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/admin/services", new { id = vaccineServiceId, nameAr = "لقاح", category = "vaccination", defaultPrice = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var vaccinationId = Guid.CreateVersion7();
        (await PostAsync(client, "/vaccinations", new
        {
            id = vaccinationId, customerId, visitId, serviceId = vaccineServiceId,
            vaccineType = "لقاح", dateGiven = "2026-06-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"vaxfr-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Billed → money/identity fields frozen, clinical fields still editable.
        var delete = await SendAsync(client, HttpMethod.Delete, $"/vaccinations/{vaccinationId}", null);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict, "a billed vaccination cannot be removed from the visit");
        (await delete.Content.ReadAsStringAsync()).Should().Contain("vaccination_billed");

        var reprice = await SendAsync(client, HttpMethod.Patch, $"/vaccinations/{vaccinationId}", new { price = 99m });
        reprice.StatusCode.Should().Be(HttpStatusCode.Conflict, "a billed vaccination cannot be re-priced");
        (await reprice.Content.ReadAsStringAsync()).Should().Contain("vaccination_billed");

        (await SendAsync(client, HttpMethod.Patch, $"/vaccinations/{vaccinationId}", new { nextDueDate = "2026-09-01" }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the clinical schedule stays editable after billing");

        // An unbilled vaccination is still the vet's to remove.
        var freeVax = Guid.CreateVersion7();
        (await PostAsync(client, "/vaccinations", new
        {
            id = freeVax, customerId, visitId, serviceId = vaccineServiceId,
            vaccineType = "لقاح آخر", dateGiven = "2026-06-01",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, HttpMethod.Delete, $"/vaccinations/{freeVax}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- M23: checkup fee, night stays & billable in-clinic meds on the invoice rail ----
    // NOTE: these (and the older tests above) lean on AdminTestSeed leaving default_checkup_fee at
    // 0 — visits created without an explicit checkupFeeApplied carry no chargeable fee, so the M23
    // assembly blocks stay inert for the pre-M23 assertions. Fees here are always explicit.

    [Fact]
    public async Task Pos_LinkedVisit_AssemblesCareCharges_AndBillableInClinicRx_WithoutDoubleDeduct()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress", checkupFeeApplied = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Billable in-clinic med (deducts at recording) + a non-billable one (never bills).
        var billableRxId = Guid.CreateVersion7();
        (await PostAsync(client, "/prescriptions", new { id = billableRxId, visitId, productId, dispenseType = "administered_in_clinic", billable = true, quantity = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/prescriptions", new { id = Guid.CreateVersion7(), visitId, productId, dispenseType = "administered_in_clinic", quantity = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A closed two-night stay (explicit rate — no settings dance).
        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new { id = stayId, visitId, careType = "medical", nightlyRate = 80m, checkInAt = "2026-06-01T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-03T06:00:00Z" }))
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
            idempotencyKey = $"care-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var items = await db.InvoiceItems.AsNoTracking().Where(it => it.InvoiceId == invoiceId).ToListAsync();
        items.Should().HaveCount(3, "the billable med, the checkup fee, and the closed stay assemble; the non-billable med never bills");

        var rxLine = items.Single(it => it.PrescriptionId == billableRxId);
        rxLine.LineTotal.Should().Be(50m, "2 × the 25 catalog price");

        var checkupService = await db.Services.AsNoTracking().SingleAsync(s => s.Category == ServiceCategories.Checkup);
        var feeLine = items.Single(it => it.CheckupFeeVisitId == visitId);
        feeLine.ServiceId.Should().Be(checkupService.Id, "the fee line bills the per-environment system service");
        feeLine.Quantity.Should().Be(1m);
        feeLine.LineTotal.Should().Be(30m);

        var stayLine = items.Single(it => it.NightStayId == stayId);
        stayLine.Quantity.Should().Be(2m, "quantity = nights");
        stayLine.UnitPrice.Should().Be(80m, "unit price = the stay's rate snapshot");
        stayLine.LineTotal.Should().Be(160m);

        var stock = await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == StockLocation.Warehouse && s.LocationId == warehouseId && s.ProductId == productId)
            .Select(s => s.Quantity).FirstAsync();
        stock.Should().Be(97m, "both administered meds deducted at recording (2 + 1); issuance must NOT deduct the billable one again");

        // Everything is billed — a second invoice for the visit has nothing left.
        var secondId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = secondId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"care2-{secondId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pos_ExplicitCareChargeLines_TillWinsPrice_ServerWinsQuantity()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the deduction warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress", checkupFeeApplied = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new { id = stayId, visitId, careType = "hotel", nightlyRate = 80m, checkInAt = "2026-06-01T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-03T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK); // 2 nights

        // The till discounts the fee to 25 and the rate to 70 (−10 line discount); quantities are
        // tampered with (9) to prove the server wins. No product/service ids — the server resolves
        // the system services.
        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId,
            customerId,
            visitId,
            discountAmount = 0m,
            items = new object[]
            {
                new { checkupFeeVisitId = visitId, quantity = 9m, unitPrice = 25m, discountAmount = 0m },
                new { nightStayId = stayId, quantity = 9m, unitPrice = 70m, discountAmount = 10m },
            },
            payments = Array.Empty<object>(),
            idempotencyKey = $"careedit-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var items = await db.InvoiceItems.AsNoTracking().Where(it => it.InvoiceId == invoiceId).ToListAsync();
        items.Should().HaveCount(2, "the explicit back-linked lines suppress re-assembly");

        var feeLine = items.Single(it => it.CheckupFeeVisitId == visitId);
        feeLine.Quantity.Should().Be(1m, "a checkup fee always bills once");
        feeLine.UnitPrice.Should().Be(25m, "the till's price adjustment is honoured");

        var stayLine = items.Single(it => it.NightStayId == stayId);
        stayLine.Quantity.Should().Be(2m, "nights are server-authoritative, not the request's 9");
        stayLine.UnitPrice.Should().Be(70m);
        stayLine.LineTotal.Should().Be(130m, "2 × 70 − 10");
    }

    [Fact]
    public async Task Pos_OpenVisit_CheckupFeeNotChargeable()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the deduction warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        // Created OPEN — بدء الكشف never confirmed the fee, so the proposed 30 is not chargeable.
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, checkupFeeApplied = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var emptyId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = emptyId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"openfee-{emptyId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict, "an open visit's fee never assembles");

        var explicitId = Guid.CreateVersion7();
        var explicitResp = await PostAsync(client, "/pos/invoices", new
        {
            id = explicitId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { checkupFeeVisitId = visitId, quantity = 1m, unitPrice = 30m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"openfee2-{explicitId:N}",
        });
        explicitResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(explicitResp)).Should().Be("checkup_fee_not_started");
    }

    [Fact]
    public async Task Pos_OpenNightStay_NotAssembled_AndExplicitRejected()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the deduction warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress", checkupFeeApplied = 0m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new { id = stayId, visitId, careType = "icu", nightlyRate = 80m, checkInAt = "2026-06-01T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK); // never closed

        var emptyId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = emptyId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"openstay-{emptyId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict, "an open stay has no nights to bill");

        var explicitId = Guid.CreateVersion7();
        var explicitResp = await PostAsync(client, "/pos/invoices", new
        {
            id = explicitId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { nightStayId = stayId, quantity = 1m, unitPrice = 80m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"openstay2-{explicitId:N}",
        });
        explicitResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(explicitResp)).Should().Be("night_stay_open");
    }

    [Fact]
    public async Task CompletionBackstop_ThenPos_NeverDoubleCharges()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the deduction warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress", checkupFeeApplied = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new { id = stayId, visitId, careType = "medical", nightlyRate = 80m, checkInAt = "2026-06-01T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-02T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK); // 1 night

        // The visit never reaches the till — completion backstops both charges to the ledger.
        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync())
                .Should().Be(110m, "30 checkup + 1 × 80 night");
        }

        // A later POS ring-up of the (completed) visit finds nothing to bill…
        var emptyId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = emptyId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"after-{emptyId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // …and explicit back-links are rejected against the backstop's ledger keys.
        var feeId = Guid.CreateVersion7();
        var feeResp = await PostAsync(client, "/pos/invoices", new
        {
            id = feeId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { checkupFeeVisitId = visitId, quantity = 1m, unitPrice = 30m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"after2-{feeId:N}",
        });
        feeResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(feeResp)).Should().Be("checkup_fee_already_billed");

        var stayLineId = Guid.CreateVersion7();
        var stayResp = await PostAsync(client, "/pos/invoices", new
        {
            id = stayLineId, customerId, visitId, discountAmount = 0m,
            items = new object[] { new { nightStayId = stayId, quantity = 1m, unitPrice = 80m } },
            payments = Array.Empty<object>(),
            idempotencyKey = $"after3-{stayLineId:N}",
        });
        stayResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(stayResp)).Should().Be("night_stay_already_billed");

        // The backstopped fee is frozen on the visit too.
        var patchResp = await SendAsync(client, HttpMethod.Patch, $"/visits/{visitId}", new { checkupFeeApplied = 50m });
        patchResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(patchResp)).Should().Be("visit_locked"); // terminal visit; the fee guard backs non-terminal edits
    }

    [Fact]
    public async Task PosBillsCareCharges_ThenCompletion_PostsNothingExtra()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await SeedWarehouseStockAsync(scope, 100m, purchase: 10m, selling: 25m); // POS resolves the deduction warehouse up-front
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var visitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new { id = visitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "in_progress", checkupFeeApplied = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new { id = stayId, visitId, careType = "medical", nightlyRate = 80m, checkInAt = "2026-06-01T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-03T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK); // 2 nights

        // The cashier rings the visit up BEFORE completion — fee + stay bill on the invoice.
        var invoiceId = Guid.CreateVersion7();
        (await PostAsync(client, "/pos/invoices", new
        {
            id = invoiceId, customerId, visitId, discountAmount = 0m,
            items = Array.Empty<object>(), payments = Array.Empty<object>(),
            idempotencyKey = $"posfirst-{invoiceId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The freeze applies the moment the invoice line exists (visit still in_progress).
        var patchResp = await SendAsync(client, HttpMethod.Patch, $"/visits/{visitId}", new { checkupFeeApplied = 50m });
        patchResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(patchResp)).Should().Be("checkup_fee_billed");

        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.LedgerEntries.AsNoTracking().CountAsync(e =>
                e.EntryType == LedgerEntryType.CheckupFee || e.EntryType == LedgerEntryType.NightStay))
            .Should().Be(0, "the invoice billed both charges; the completion backstop must post nothing");
        (await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync())
            .Should().Be(190m, "one invoice entry: 30 fee + 2 × 80 nights, unpaid");
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

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
        => await SendAsync(client, HttpMethod.Post, path, body, idemKey);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, object? body, string? idemKey = null)
    {
        var request = new HttpRequestMessage(method, path)
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
