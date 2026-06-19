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

namespace VetSystem.Tests.OperatingExpenses;

/// <summary>
/// Operating-expenses CRUD (water/electricity/…) and their effect on the clinic-profit report: incurred
/// expenses subtract from net profit for the period; unpaid ones plus AP ledger balances make up the
/// "amount owed to others" snapshot.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OperatingExpensesIntegrationTests
{
    [Fact]
    public async Task OperatingExpense_Crud_AndMarkPaid()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var id = Guid.CreateVersion7();
        var create = await PostAsync(client, "/operating-expenses", new
        {
            id,
            category = OperatingExpenseCategory.Electricity,
            amount = 250m,
            incurredOn = "2026-06-15",
            paid = false,
            note = "فاتورة كهرباء يونيو",
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("paid").GetBoolean().Should().BeFalse();
        created.GetProperty("paidAt").ValueKind.Should().Be(JsonValueKind.Null);

        // Mark paid via PATCH → stamps paidAt.
        var paid = await PatchAsync(client, $"/operating-expenses/{id}", new { paid = true });
        paid.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterPay = await paid.Content.ReadFromJsonAsync<JsonElement>();
        afterPay.GetProperty("paid").GetBoolean().Should().BeTrue();
        afterPay.GetProperty("paidAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        // Edit the amount.
        (await PatchAsync(client, $"/operating-expenses/{id}", new { amount = 300m })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var paidList = await client.GetFromJsonAsync<JsonElement>("/operating-expenses?paid=true");
        paidList.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("amount").GetDecimal().Should().Be(300m);

        (await DeleteAsync(client, $"/operating-expenses/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var empty = await client.GetFromJsonAsync<JsonElement>("/operating-expenses");
        empty.GetArrayLength().Should().Be(0, "the expense was soft-deleted");
    }

    [Fact]
    public async Task ClinicProfits_SubtractsExpenses_AndReportsOwedToOthers()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // 100 paid (water) + 50 unpaid (electricity), both in June 2026.
        (await PostAsync(client, "/operating-expenses", new
        {
            id = Guid.CreateVersion7(), category = OperatingExpenseCategory.Water,
            amount = 100m, incurredOn = "2026-06-10", paid = true, note = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/operating-expenses", new
        {
            id = Guid.CreateVersion7(), category = OperatingExpenseCategory.Electricity,
            amount = 50m, incurredOn = "2026-06-20", paid = false, note = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // A supplier the center owes 40.
        await SeedSupplierPayableAsync(scope, 40m);

        var report = await client.GetFromJsonAsync<JsonElement>(
            "/reports/clinic-profits?from=2026-06-01&to=2026-06-30");

        report.GetProperty("netProfit").GetDecimal().Should().Be(0m, "no sales seeded");
        report.GetProperty("operatingExpenses").GetDecimal().Should().Be(150m, "100 + 50 incurred in the window");
        report.GetProperty("netOperatingProfit").GetDecimal().Should().Be(-150m, "0 net − 150 expenses");

        report.GetProperty("payablesSuppliers").GetDecimal().Should().Be(40m);
        report.GetProperty("payablesUnpaidExpenses").GetDecimal().Should().Be(50m, "only the unpaid expense");
        report.GetProperty("payablesOutstanding").GetDecimal().Should().Be(90m, "40 supplier + 50 unpaid expense");
        report.GetProperty("netAfterObligations").GetDecimal().Should().Be(-240m, "-150 net − 90 owed");
    }

    [Fact]
    public async Task OperatingExpenses_RequirePermission()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var cashier = await SeedUserWithRoleAsync(scope, RoleKey.Cashier); // no operating_expenses.manage
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, cashier, role: RoleKey.Cashier);

        var resp = await PostAsync(client, "/operating-expenses", new
        {
            id = Guid.CreateVersion7(), category = OperatingExpenseCategory.Water,
            amount = 10m, incurredOn = "2026-06-01", paid = false, note = (string?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
        => SendAsync(client, HttpMethod.Post, path, body);

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, object body)
        => SendAsync(client, HttpMethod.Patch, path, body);

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path)
        => SendAsync(client, HttpMethod.Delete, path, null);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task SeedSupplierPayableAsync(PgTestScope scope, decimal balance)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var now = DateTimeOffset.UtcNow;
        var supplierId = Guid.CreateVersion7();
        db.Suppliers.Add(new Supplier
        {
            Id = supplierId, EnvironmentId = scope.EnvironmentId, Name = "Acme Pharma",
            CreatedAt = now, UpdatedAt = now,
        });
        db.SupplierLedgers.Add(new SupplierLedger
        {
            Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId, SupplierId = supplierId,
            Balance = balance, Status = LedgerStatus.HasDebt, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedUserWithRoleAsync(PgTestScope scope, string roleKey)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Cashier",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"C{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
