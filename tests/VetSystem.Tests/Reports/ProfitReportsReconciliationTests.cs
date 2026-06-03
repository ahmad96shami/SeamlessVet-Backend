using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M20 task 5 — the three profit reports must agree with the clinic-profits report on the same window
/// (exit criterion: "profit = revenue − COGS reconciles against clinic-profits"). A controlled invoice
/// graph (one in-clinic POS invoice with a product + a service line, one field invoice, plus a voided
/// pair that must drop out) is seeded directly, then all four reports are read over the API:
/// <list type="bullet">
/// <item>pharmacy <c>Cost</c> == clinic-profits <c>Cogs</c> (both are Σ cost_price×qty over product lines);</item>
/// <item>in-clinic <c>Profit</c> + field <c>Profit</c> == clinic-profits <c>NetProfit</c> (every effective
///       invoice here carries a visit, so the two visit slices partition the whole);</item>
/// <item>the farm/clinic slicer routes the field (farm) and in-clinic (clinic) visits to the right side.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProfitReportsReconciliationTests
{
    [Fact]
    public async Task ProfitReports_ReconcileAgainstClinicProfits_ToTheCent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var seed = await SeedInvoiceGraphAsync(scope, admin.Id);

        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var clinic = await GetAsync<ClinicProfitsReportResponse>(client, "/reports/clinic-profits");
        var pharmacy = await GetAsync<PharmacyProfitReportResponse>(client, "/reports/pharmacy-profit");
        var inClinic = await GetAsync<VisitProfitReportResponse>(client, "/reports/in-clinic-visit-profit");
        var field = await GetAsync<VisitProfitReportResponse>(client, "/reports/field-visit-profit");

        // Clinic-profits baseline: revenue 90 + 100 = 190, COGS (10×2)+(10×4) = 60, net 130.
        clinic.Revenue.Should().Be(190m);
        clinic.Cogs.Should().Be(60m);
        clinic.NetProfit.Should().Be(130m);

        // Pharmacy: product lines only — one product, qty 6, revenue 150, cost 60, profit 90.
        pharmacy.Revenue.Should().Be(150m);
        pharmacy.Cost.Should().Be(60m);
        pharmacy.Profit.Should().Be(90m);
        pharmacy.TotalCount.Should().Be(1);
        pharmacy.Rows.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ProductId = seed.ProductId, QuantitySold = 6m, Revenue = 150m, Cost = 60m, Profit = 90m });
        pharmacy.Cost.Should().Be(clinic.Cogs, "pharmacy cost is the same Σ cost_price×qty the clinic-profits COGS sums");

        // In-clinic visit profit: V1 only (the voided invoice on V1 drops out).
        inClinic.VisitCount.Should().Be(1);
        inClinic.Revenue.Should().Be(90m);
        inClinic.Cogs.Should().Be(20m);
        inClinic.Profit.Should().Be(70m);
        inClinic.Rows.Should().ContainSingle().Which.VisitId.Should().Be(seed.InClinicVisitId);

        // Field visit profit: V2 only.
        field.VisitCount.Should().Be(1);
        field.Revenue.Should().Be(100m);
        field.Cogs.Should().Be(40m);
        field.Profit.Should().Be(60m);
        field.Rows.Should().ContainSingle().Which.VisitId.Should().Be(seed.FieldVisitId);

        // The reconciliation: the two visit slices add up to the clinic-profits net (no walk-ins here).
        (inClinic.Profit + field.Profit).Should().Be(clinic.NetProfit);
        (inClinic.Revenue + field.Revenue).Should().Be(clinic.Revenue);
        (inClinic.Cogs + field.Cogs).Should().Be(clinic.Cogs);
    }

    [Fact]
    public async Task VisitProfit_FarmClinicSlicer_RoutesByFarmId()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var seed = await SeedInvoiceGraphAsync(scope, admin.Id);

        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // The field visit carries a FarmId → farm scope shows it, clinic scope is empty.
        var fieldFarm = await GetAsync<VisitProfitReportResponse>(client, "/reports/field-visit-profit?scope=farm");
        fieldFarm.Scope.Should().Be("farm");
        fieldFarm.VisitCount.Should().Be(1);
        fieldFarm.Rows.Should().ContainSingle().Which.FarmId.Should().Be(seed.FarmId);

        var fieldClinic = await GetAsync<VisitProfitReportResponse>(client, "/reports/field-visit-profit?scope=clinic");
        fieldClinic.VisitCount.Should().Be(0);
        fieldClinic.Rows.Should().BeEmpty();

        // The in-clinic visit has no FarmId → clinic scope shows it, farm scope is empty.
        var inClinicClinic = await GetAsync<VisitProfitReportResponse>(client, "/reports/in-clinic-visit-profit?scope=clinic");
        inClinicClinic.VisitCount.Should().Be(1);

        var inClinicFarm = await GetAsync<VisitProfitReportResponse>(client, "/reports/in-clinic-visit-profit?scope=farm");
        inClinicFarm.VisitCount.Should().Be(0);
    }

    // ---- seeding ----

    private sealed record SeedResult(Guid ProductId, Guid FarmId, Guid InClinicVisitId, Guid FieldVisitId);

    /// <summary>
    /// Seeds, in one env: a customer + farm, a product (cost 10 / sell 25) and a service, an in-clinic
    /// visit billed by a POS invoice (product qty 2 + service) and a field visit billed by a field
    /// invoice (product qty 4), plus a voided POS invoice on the in-clinic visit that must drop out.
    /// </summary>
    private static async Task<SeedResult> SeedInvoiceGraphAsync(PgTestScope scope, Guid adminId)
    {
        await using var db = NewContext(scope, adminId);
        var env = scope.EnvironmentId;
        var now = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

        var customerId = Guid.CreateVersion7();
        db.Customers.Add(new Customer
        {
            Id = customerId, EnvironmentId = env, Type = CustomerType.PoultryFarm,
            FullName = "Profit Reco Farm", PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            CreatedAt = now, UpdatedAt = now,
        });

        var farmId = Guid.CreateVersion7();
        db.Farms.Add(new Farm
        {
            Id = farmId, EnvironmentId = env, CustomerId = customerId, Name = "حظيرة أ",
            Kind = FarmKind.Poultry, CreatedAt = now, UpdatedAt = now,
        });

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId, EnvironmentId = env, NameAr = "دواء", Category = ProductCategory.Medication,
            PurchasePrice = 10m, SellingPrice = 25m, CreatedAt = now, UpdatedAt = now,
        });

        var serviceId = Guid.CreateVersion7();
        db.Services.Add(new Service
        {
            Id = serviceId, EnvironmentId = env, NameAr = "كشف", DefaultPrice = 40m,
            CreatedAt = now, UpdatedAt = now,
        });

        var inClinicVisitId = Guid.CreateVersion7();
        db.Visits.Add(new Visit
        {
            Id = inClinicVisitId, EnvironmentId = env, VisitType = VisitType.InClinic, VisitNumber = "A-1",
            CustomerId = customerId, FarmId = null, DoctorId = adminId, Status = VisitStatus.Completed,
            StartedAt = now, EndedAt = now, CreatedAt = now, UpdatedAt = now,
        });

        var fieldVisitId = Guid.CreateVersion7();
        db.Visits.Add(new Visit
        {
            Id = fieldVisitId, EnvironmentId = env, VisitType = VisitType.Field, VisitNumber = "A-2",
            CustomerId = customerId, FarmId = farmId, DoctorId = adminId, Status = VisitStatus.Completed,
            StartedAt = now, EndedAt = now, CreatedAt = now, UpdatedAt = now,
        });

        // I1 — in-clinic POS: product qty 2 (rev 50 / cost 20) + service (rev 40 / cost 0). Total 90.
        var inClinicInvoiceId = AddInvoice(db, env, adminId, now, InvoiceType.Pos, customerId, inClinicVisitId, null,
            subtotal: 90m, total: 90m, status: InvoiceStatus.Issued, number: "A-1001", voidOf: null);
        AddProductLine(db, env, now, inClinicInvoiceId, productId, qty: 2m, unit: 25m, cost: 10m, lineTotal: 50m);
        AddServiceLine(db, env, now, inClinicInvoiceId, serviceId, unit: 40m, lineTotal: 40m);

        // I2 — field invoice: product qty 4 (rev 100 / cost 40). Total 100, attributed to the farm.
        var fieldInvoiceId = AddInvoice(db, env, adminId, now, InvoiceType.Field, customerId, fieldVisitId, farmId,
            subtotal: 100m, total: 100m, status: InvoiceStatus.Issued, number: "A-1002", voidOf: null);
        AddProductLine(db, env, now, fieldInvoiceId, productId, qty: 4m, unit: 25m, cost: 10m, lineTotal: 100m);

        // I3 — a POS invoice on the in-clinic visit that is later voided; I4 reverses it. Both must drop
        // out of every profit report, so they never reach the assertions above.
        var voidedOriginalId = AddInvoice(db, env, adminId, now, InvoiceType.Pos, customerId, inClinicVisitId, null,
            subtotal: 250m, total: 250m, status: InvoiceStatus.Issued, number: "A-1003", voidOf: null);
        AddProductLine(db, env, now, voidedOriginalId, productId, qty: 10m, unit: 25m, cost: 10m, lineTotal: 250m);
        AddInvoice(db, env, adminId, now, InvoiceType.Pos, customerId, inClinicVisitId, null,
            subtotal: -250m, total: -250m, status: InvoiceStatus.Void, number: null, voidOf: voidedOriginalId);

        await db.SaveChangesAsync();
        return new SeedResult(productId, farmId, inClinicVisitId, fieldVisitId);
    }

    private static Guid AddInvoice(
        ApplicationDbContext db, Guid env, Guid issuedBy, DateTimeOffset now, string type, Guid customerId,
        Guid? visitId, Guid? farmId, decimal subtotal, decimal total, string status, string? number, Guid? voidOf)
    {
        var id = Guid.CreateVersion7();
        db.Invoices.Add(new Invoice
        {
            Id = id, EnvironmentId = env, InvoiceType = type, CustomerId = customerId, FarmId = farmId,
            VisitId = visitId, Number = number, Subtotal = subtotal, DiscountAmount = 0m, TaxAmount = 0m,
            Total = total, Status = status, IssuedBy = issuedBy, IssuedAt = now,
            IdempotencyKey = $"reco-{id:N}", VoidOfInvoiceId = voidOf, CreatedAt = now, UpdatedAt = now,
        });
        return id;
    }

    private static void AddProductLine(
        ApplicationDbContext db, Guid env, DateTimeOffset now, Guid invoiceId, Guid productId,
        decimal qty, decimal unit, decimal cost, decimal lineTotal) =>
        db.InvoiceItems.Add(new InvoiceItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = env, InvoiceId = invoiceId, ProductId = productId,
            Quantity = qty, UnitPrice = unit, CostPrice = cost, DiscountAmount = 0m, LineTotal = lineTotal,
            CreatedAt = now, UpdatedAt = now,
        });

    private static void AddServiceLine(
        ApplicationDbContext db, Guid env, DateTimeOffset now, Guid invoiceId, Guid serviceId,
        decimal unit, decimal lineTotal) =>
        db.InvoiceItems.Add(new InvoiceItem
        {
            Id = Guid.CreateVersion7(), EnvironmentId = env, InvoiceId = invoiceId, ServiceId = serviceId,
            Quantity = 1m, UnitPrice = unit, CostPrice = 0m, DiscountAmount = 0m, LineTotal = lineTotal,
            CreatedAt = now, UpdatedAt = now,
        });

    // ---- helpers ----

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var resp = await client.GetAsync(path);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0} should succeed", path);
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static HttpClient AuthedClient(VetApiFactory factory, User admin)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
