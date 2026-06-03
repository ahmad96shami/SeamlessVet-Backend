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

namespace VetSystem.Tests.Suppliers;

/// <summary>
/// M19 exit criteria — suppliers, purchase invoices (receive-goods + payable), supplier payments
/// (incl. cheque), customer cheque (immediate settlement + metadata), and the append-only supplier
/// ledger, all over a real Postgres env.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SuppliersIntegrationTests
{
    [Fact]
    public async Task PurchaseInvoice_ReceivesStock_AndPostsSupplierPayable()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndProductAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var supplierId = await CreateSupplierAsync(client);

        var purchaseId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/purchase-invoices", new
        {
            id = purchaseId,
            supplierId,
            number = "SUP-INV-001",
            discountAmount = 0m,
            taxAmount = (decimal?)null,
            items = new[] { new { productId, quantity = 5m, unitCost = 8m, discountAmount = 0m } },
            idempotencyKey = $"pi-{purchaseId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var invoice = await db.PurchaseInvoices.AsNoTracking().FirstAsync(p => p.Id == purchaseId);
        invoice.Total.Should().Be(40m, "5 × 8");

        var item = await db.PurchaseInvoiceItems.AsNoTracking().SingleAsync(it => it.PurchaseInvoiceId == purchaseId);
        item.UnitCost.Should().Be(8m, "unit cost is snapshotted on the purchase line");
        item.LineTotal.Should().Be(40m);

        var stock = await db.StockItems.AsNoTracking()
            .Where(s => s.LocationType == StockLocation.Warehouse && s.LocationId == warehouseId && s.ProductId == productId)
            .Select(s => s.Quantity).FirstAsync();
        stock.Should().Be(5m, "the purchase invoice receives 5 units into the warehouse (delta-only)");

        var movement = await db.InventoryMovements.AsNoTracking()
            .SingleAsync(m => m.PurchaseInvoiceId == purchaseId);
        movement.MovementType.Should().Be(MovementType.Receive);
        movement.QuantityDelta.Should().Be(5m);
        movement.ToLocationId.Should().Be(warehouseId);

        var ledger = await db.SupplierLedgers.AsNoTracking().FirstAsync(l => l.SupplierId == supplierId);
        ledger.Balance.Should().Be(40m, "the payable rises by the invoice total");
        ledger.Status.Should().Be(LedgerStatus.HasDebt);

        var entry = await db.SupplierLedgerEntries.AsNoTracking().SingleAsync(e => e.PurchaseInvoiceId == purchaseId);
        entry.EntryType.Should().Be(SupplierLedgerEntryType.PurchaseInvoice);
        entry.Amount.Should().Be(40m);
        entry.BalanceAfter.Should().Be(40m);
    }

    [Fact]
    public async Task PurchaseInvoice_IdempotentReplay_DoesNotDoubleReceiveOrPost()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (warehouseId, productId) = await SeedWarehouseAndProductAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var supplierId = await CreateSupplierAsync(client);
        var body = new
        {
            id = Guid.CreateVersion7(),
            supplierId,
            number = "SUP-INV-RE",
            discountAmount = 0m,
            taxAmount = (decimal?)null,
            items = new[] { new { productId, quantity = 3m, unitCost = 10m, discountAmount = 0m } },
            idempotencyKey = "stable-purchase-key-001",
        };
        var key = $"pi-hdr-{Guid.NewGuid():N}"[..32];

        (await PostAsync(client, "/purchase-invoices", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/purchase-invoices", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.PurchaseInvoices.AsNoTracking().CountAsync(p => p.SupplierId == supplierId))
            .Should().Be(1, "the same idempotency key collapses retries to one purchase invoice");

        var stock = await db.StockItems.AsNoTracking()
            .Where(s => s.LocationId == warehouseId && s.ProductId == productId)
            .Select(s => s.Quantity).FirstAsync();
        stock.Should().Be(3m, "stock is received exactly once");

        (await db.SupplierLedgers.AsNoTracking().Where(l => l.SupplierId == supplierId).Select(l => l.Balance).FirstAsync())
            .Should().Be(30m, "the payable is posted exactly once");
    }

    [Fact]
    public async Task SupplierPayment_ByCheque_ReducesBalance_AndStoresMetadata()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseAndProductAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var supplierId = await CreateSupplierAsync(client);
        var purchaseId = Guid.CreateVersion7();
        (await PostAsync(client, "/purchase-invoices", new
        {
            id = purchaseId,
            supplierId,
            discountAmount = 0m,
            taxAmount = (decimal?)null,
            items = new[] { new { productId, quantity = 5m, unitCost = 8m, discountAmount = 0m } },
            idempotencyKey = $"pi-{purchaseId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentId = Guid.CreateVersion7();
        var resp = await PostAsync(client, $"/suppliers/{supplierId}/payments", new
        {
            id = paymentId,
            amount = 40m,
            method = "cheque",
            notes = "settle in full",
            chequeNumber = "CHQ-7788",
            chequeBank = "Bank of Palestine",
            chequeDueDate = "2026-07-01",
            idempotencyKey = $"sp-{paymentId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var ledger = await db.SupplierLedgers.AsNoTracking().FirstAsync(l => l.SupplierId == supplierId);
        ledger.Balance.Should().Be(0m, "a 40 cheque payment clears the 40 payable");
        ledger.Status.Should().Be(LedgerStatus.Open);

        var payment = await db.SupplierPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        payment.Method.Should().Be("cheque");
        payment.ChequeNumber.Should().Be("CHQ-7788");
        payment.ChequeBank.Should().Be("Bank of Palestine");
        payment.ChequeDueDate.Should().Be(new DateOnly(2026, 7, 1));

        var entry = await db.SupplierLedgerEntries.AsNoTracking().SingleAsync(e => e.SupplierPaymentId == paymentId);
        entry.EntryType.Should().Be(SupplierLedgerEntryType.Payment);
        entry.Amount.Should().Be(-40m, "a payment reduces the payable");
    }

    [Fact]
    public async Task SupplierPayment_IdempotentReplay_DoesNotDoublePost()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseAndProductAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var supplierId = await CreateSupplierAsync(client);
        (await PostAsync(client, "/purchase-invoices", new
        {
            id = Guid.CreateVersion7(),
            supplierId,
            discountAmount = 0m,
            taxAmount = (decimal?)null,
            items = new[] { new { productId, quantity = 5m, unitCost = 8m, discountAmount = 0m } },
            idempotencyKey = $"pi-{Guid.NewGuid():N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var body = new
        {
            id = Guid.CreateVersion7(),
            amount = 10m,
            method = "cash",
            notes = (string?)null,
            chequeNumber = (string?)null,
            chequeBank = (string?)null,
            chequeDueDate = (string?)null,
            idempotencyKey = "stable-supplier-payment-key",
        };
        var key = $"sp-hdr-{Guid.NewGuid():N}"[..32];

        (await PostAsync(client, $"/suppliers/{supplierId}/payments", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/suppliers/{supplierId}/payments", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.SupplierPayments.AsNoTracking().CountAsync(p => p.SupplierId == supplierId))
            .Should().Be(1, "the same idempotency key collapses retries to one payment");
        (await db.SupplierLedgers.AsNoTracking().Where(l => l.SupplierId == supplierId).Select(l => l.Balance).FirstAsync())
            .Should().Be(30m, "40 payable − 10 paid once = 30 (not double-debited)");
    }

    [Fact]
    public async Task CustomerCheque_OnPos_SettlesImmediately_AndStoresMetadata()
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
            items = new[] { new { productId, quantity = 2m, discountAmount = 0m } }, // total 50
            payments = new[]
            {
                new { method = "cheque", amount = 50m, chequeNumber = "CHQ-1001", chequeBank = "Cairo Amman", chequeDueDate = "2026-08-15" },
            },
            idempotencyKey = $"chq-{invoiceId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var balance = await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).FirstAsync();
        balance.Should().Be(0m, "a cheque is an immediate (non-credit) method, so it settles the invoice in full");

        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.InvoiceId == invoiceId);
        payment.Method.Should().Be("cheque");
        payment.ChequeNumber.Should().Be("CHQ-1001");
        payment.ChequeBank.Should().Be("Cairo Amman");
        payment.ChequeDueDate.Should().Be(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public async Task CustomerCheque_OnReceiptVoucher_StoresMetadata_AndCreditsLedger()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var customerId = await CreateCustomerAsync(client);
        var voucherId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/receipt-vouchers", new
        {
            id = voucherId,
            customerId,
            farmId = (Guid?)null,
            amount = 30m,
            method = "cheque",
            notes = "partial",
            idempotencyKey = $"rv-{voucherId:N}",
            chequeNumber = "CHQ-2002",
            chequeBank = "Quds Bank",
            chequeDueDate = "2026-09-01",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var voucher = await db.ReceiptVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);
        voucher.Method.Should().Be("cheque");
        voucher.ChequeNumber.Should().Be("CHQ-2002");
        voucher.ChequeDueDate.Should().Be(new DateOnly(2026, 9, 1));

        var balance = await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).FirstAsync();
        balance.Should().Be(-30m, "a receipt voucher credits the ledger (negative balance = credit on account)");
    }

    [Fact]
    public async Task Supplier_Statement_Renders_WithRunningBalance()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var (_, productId) = await SeedWarehouseAndProductAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var supplierId = await CreateSupplierAsync(client);
        (await PostAsync(client, "/purchase-invoices", new
        {
            id = Guid.CreateVersion7(),
            supplierId,
            discountAmount = 0m,
            taxAmount = (decimal?)null,
            items = new[] { new { productId, quantity = 5m, unitCost = 8m, discountAmount = 0m } },
            idempotencyKey = $"pi-{Guid.NewGuid():N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var payId = Guid.CreateVersion7();
        (await PostAsync(client, $"/suppliers/{supplierId}/payments", new
        {
            id = payId,
            amount = 15m,
            method = "cash",
            notes = (string?)null,
            chequeNumber = (string?)null,
            chequeBank = (string?)null,
            chequeDueDate = (string?)null,
            idempotencyKey = $"sp-{payId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var statement = await client.GetFromJsonAsync<JsonElement>($"/suppliers/{supplierId}/statement");
        statement.GetProperty("supplierId").GetGuid().Should().Be(supplierId);
        statement.GetProperty("closingBalance").GetDecimal().Should().Be(25m, "40 payable − 15 paid");
        statement.GetProperty("entries").GetArrayLength().Should().Be(2, "one purchase invoice + one payment");
    }

    [Fact]
    public async Task Suppliers_Write_RequiresPermission()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope); // seeds roles + permissions for the env
        var fieldVet = await SeedUserWithRoleAsync(scope, RoleKey.VetField); // no suppliers.write granted
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, fieldVet, role: RoleKey.VetField);

        var resp = await PostAsync(client, "/suppliers", new { id = Guid.CreateVersion7(), name = "Blocked Co." });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "suppliers.write gates supplier writes");
    }

    // ---- helpers ----

    private static HttpClient AuthedClient(VetApiFactory factory, User user, string role = "admin")
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, role));
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

    private static async Task<Guid> CreateSupplierAsync(HttpClient client)
    {
        var supplierId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/suppliers", new
        {
            id = supplierId,
            name = "Acme Pharma",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            taxNumber = "TX-1234",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return supplierId;
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Chq Cust",
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

    private static async Task<(Guid WarehouseId, Guid ProductId)> SeedWarehouseAndProductAsync(PgTestScope scope)
    {
        await using var db = NewContext(scope, null);
        var now = DateTimeOffset.UtcNow;

        var warehouseId = Guid.CreateVersion7();
        db.Warehouses.Add(new Warehouse { Id = warehouseId, EnvironmentId = scope.EnvironmentId, Name = "Central", CreatedAt = now, UpdatedAt = now });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "دواء",
            Category = ProductCategory.Medication, PurchasePrice = 8m, SellingPrice = 20m,
            CreatedAt = now, UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (warehouseId, productId);
    }

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

    private static async Task<User> SeedUserWithRoleAsync(PgTestScope scope, string roleKey)
    {
        await using var db = NewContext(scope, null);
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Field Vet",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"F{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
