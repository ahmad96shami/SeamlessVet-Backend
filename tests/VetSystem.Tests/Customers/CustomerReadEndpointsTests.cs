using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Customers;

/// <summary>
/// BACKEND_PREREQS §3 — the W3 web customer reads (<c>GET /customers</c>, <c>GET /customers/{id}</c>).
/// Verifies the design-driven enrichment: each row carries the joined 1:1 ledger <c>balance</c> +
/// <c>ledgerStatus</c> (so the list shows account state without an N+1 statement call), the
/// <c>?type=</c> / <c>?assignedDoctorId=</c> / <c>?ledgerStatus=</c> filters, and that an invalid
/// ledger status is rejected with <c>409 invalid_ledger_status</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CustomerReadEndpointsTests
{
    private sealed record CustomerRow(
        Guid Id, string Type, string FullName, string? PhonePrimary, string? PhoneSecondary,
        string? Address, string? Email, string? IdNumber, string? Notes, Guid? AssignedDoctorId,
        decimal Balance, string LedgerStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    [Fact]
    public async Task List_EnrichesBalanceAndStatus_AndFiltersByTypeDoctorAndLedgerStatus()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var seed = await SeedAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthorizedClient(factory, admin);

        // All three customers, each enriched with its ledger balance + status.
        var all = await GetListAsync<CustomerRow>(client, "/customers");
        all.Should().HaveCount(3);
        all.Single(c => c.Id == seed.DebtorId).Should().Match<CustomerRow>(c =>
            c.Balance == 500m && c.LedgerStatus == LedgerStatus.HasDebt);
        all.Single(c => c.Id == seed.OpenId).Should().Match<CustomerRow>(c =>
            c.Balance == 0m && c.LedgerStatus == LedgerStatus.Open);
        all.Single(c => c.Id == seed.ClosedId).Should().Match<CustomerRow>(c =>
            c.Balance == 0m && c.LedgerStatus == LedgerStatus.Closed);

        // ?type= filter.
        var homes = await GetListAsync<CustomerRow>(client, "/customers?type=home");
        homes.Should().ContainSingle().Which.Id.Should().Be(seed.OpenId);

        // ?assignedDoctorId= filter.
        var byDoctor = await GetListAsync<CustomerRow>(client, $"/customers?assignedDoctorId={seed.DoctorId}");
        byDoctor.Should().ContainSingle().Which.Id.Should().Be(seed.DebtorId);

        // ?ledgerStatus= filter — paginates the design's "accounts with debt" view correctly.
        var debtors = await GetListAsync<CustomerRow>(client, "/customers?ledgerStatus=has_debt");
        debtors.Should().ContainSingle().Which.Id.Should().Be(seed.DebtorId);

        // GET /{id} carries the same enrichment.
        var one = await client.GetFromJsonAsync<CustomerRow>($"/customers/{seed.DebtorId}");
        one!.Balance.Should().Be(500m);
        one.LedgerStatus.Should().Be(LedgerStatus.HasDebt);

        // invalid ledgerStatus → 409 invalid_ledger_status.
        var bad = await client.GetAsync("/customers?ledgerStatus=bogus");
        bad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await bad.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("invalid_ledger_status");
    }

    // ---- helpers ----

    private sealed record Seed(Guid DoctorId, Guid DebtorId, Guid OpenId, Guid ClosedId);

    private static HttpClient AuthorizedClient(VetApiFactory factory, User admin)
    {
        var client = factory.CreateClient();
        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<List<T>> GetListAsync<T>(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<List<T>>())!;
    }

    private static async Task<Seed> SeedAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var envId = scope.EnvironmentId;
        var now = DateTimeOffset.UtcNow;

        var vetFieldRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == envId && r.Key == RoleKey.VetField);

        var doctor = new User
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = envId,
            RoleId = vetFieldRole.Id,
            FullName = "د. أحمد السوسي",
            PhonePrimary = $"+97{Guid.NewGuid():N}"[..12],
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"F{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(doctor);

        var debtor = NewCustomer(envId, CustomerType.PoultryFarm, "مزرعة الجنين للدواجن", "+970599000111", doctor.Id, now);
        var open = NewCustomer(envId, CustomerType.Home, "بيت أبو علي", null, null, now);
        var closed = NewCustomer(envId, CustomerType.CattleFarm, "مزرعة مغلقة", "+970599000222", null, now);
        db.Customers.AddRange(debtor, open, closed);

        db.Ledgers.AddRange(
            NewLedger(envId, debtor.Id, 500m, LedgerStatus.HasDebt, null, now),
            NewLedger(envId, open.Id, 0m, LedgerStatus.Open, null, now),
            NewLedger(envId, closed.Id, 0m, LedgerStatus.Closed, now, now));

        await db.SaveChangesAsync();
        return new Seed(doctor.Id, debtor.Id, open.Id, closed.Id);
    }

    private static Customer NewCustomer(
        Guid envId, string type, string fullName, string? phone, Guid? assignedDoctorId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, Type = type, FullName = fullName,
            PhonePrimary = phone, AssignedDoctorId = assignedDoctorId, CreatedAt = now, UpdatedAt = now,
        };

    private static Ledger NewLedger(
        Guid envId, Guid customerId, decimal balance, string status, DateTimeOffset? closedAt, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, CustomerId = customerId,
            Balance = balance, Status = status, ClosedAt = closedAt, CreatedAt = now, UpdatedAt = now,
        };
}
