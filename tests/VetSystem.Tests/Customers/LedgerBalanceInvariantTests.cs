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
/// M3 task 14 — property-style test: for any sequence of <c>AppendEntryAsync</c> calls,
/// <c>ledgers.balance</c> equals <c>Σ ledger_entries.amount</c> and equals the
/// <c>balance_after</c> of the last entry. Stand-in for the formal FsCheck/CsCheck suite that
/// lands with M13 (which adds the testing library for all monetary invariants). Until then,
/// a fixed-seed pseudo-random sequence is enough to catch regression in the
/// <see cref="VetSystem.Infrastructure.Ledgers.LedgerService"/> arithmetic + status transitions.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LedgerBalanceInvariantTests
{
    [Theory]
    [InlineData(42, 50)]
    [InlineData(1337, 80)]
    [InlineData(9001, 30)]
    public async Task RandomAppendSequence_PreservesBalanceInvariant(int seed, int iterations)
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var (customerId, ledgerId) = await CreateCustomerWithLedgerAsync(client);

        var rng = new Random(seed);
        var amounts = new List<decimal>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            // Signed amounts in [-500, +500] with cents precision. Skip zero (validator forbids).
            var raw = (rng.NextDouble() - 0.5) * 1000;
            var amount = Math.Round((decimal)raw, 2, MidpointRounding.AwayFromZero);
            if (amount == 0m)
            {
                amount = 1m;
            }

            await PutLedgerEntryAsync(client, ledgerId, amount, $"prop-{seed}-{i}");
            amounts.Add(amount);
        }

        await using var verifyDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var ledger = await verifyDb.Ledgers
            .AsNoTracking()
            .FirstAsync(l => l.Id == ledgerId);

        var entries = await verifyDb.LedgerEntries
            .AsNoTracking()
            .Where(e => e.LedgerId == ledgerId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        var expectedBalance = amounts.Sum();

        ledger.Balance.Should().Be(expectedBalance,
            "ledger.balance must equal the sum of all signed entry amounts (SCHEMA §2)");
        entries.Sum(e => e.Amount).Should().Be(expectedBalance,
            "every signed amount must have been persisted exactly once");
        entries[^1].BalanceAfter.Should().Be(expectedBalance,
            "balance_after on the last entry must match the running balance");
        entries.Should().HaveCount(iterations, "all entries must be persisted (idempotency keys distinct)");

        ledger.Status.Should().Be(
            expectedBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open,
            "status transitions follow the sign of the current balance");
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task AppendEntry_DuplicateIdempotencyKey_ReplaysWithoutDoubleApply()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();

        var jwt = factory.Services
            .GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);

        var (_, ledgerId) = await CreateCustomerWithLedgerAsync(client);
        var entryIdempotencyKey = $"replay-{Guid.NewGuid():N}"[..32];

        // First write — applies.
        await PutLedgerEntryAsync(client, ledgerId, 100m, entryIdempotencyKey, useUniqueSyncHeader: false);

        // Second write with the same body-level idempotency_key — must replay (the unique index
        // collapses concurrent writes). Same Idempotency-Key header would short-circuit at the
        // filter layer; we want to exercise the LedgerService replay path, so use a fresh header.
        await PutLedgerEntryAsync(client, ledgerId, 100m, entryIdempotencyKey, useUniqueSyncHeader: false);

        await using var verifyDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
        });

        var entries = await verifyDb.LedgerEntries
            .AsNoTracking()
            .Where(e => e.LedgerId == ledgerId)
            .ToListAsync();

        entries.Should().HaveCount(1, "duplicate idempotency_key must collapse to one entry");

        var ledger = await verifyDb.Ledgers.AsNoTracking().FirstAsync(l => l.Id == ledgerId);
        ledger.Balance.Should().Be(100m, "balance must not double-apply on replay");
    }

    private static async Task<(Guid CustomerId, Guid LedgerId)> CreateCustomerWithLedgerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        var req = new HttpRequestMessage(HttpMethod.Post, "/customers")
        {
            Content = JsonContent.Create(new
            {
                Id = customerId,
                Type = CustomerType.Home,
                FullName = "Property-Test Owner",
            }),
        };
        req.Headers.Add("Idempotency-Key", $"cust-{Guid.NewGuid():N}"[..32]);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(customerId);

        // Ledger row was auto-created in the same transaction (M3 task 5).
        var statementResp = await client.GetAsync($"/customers/{customerId}/statement");
        statementResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statement = await statementResp.Content.ReadFromJsonAsync<JsonElement>();
        var ledgerId = statement.GetProperty("ledgerId").GetGuid();

        return (customerId, ledgerId);
    }

    private static async Task PutLedgerEntryAsync(
        HttpClient client,
        Guid ledgerId,
        decimal amount,
        string entryIdempotencyKey,
        bool useUniqueSyncHeader = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/sync/ledger_entries")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                ledger_id = ledgerId,
                entry_type = LedgerEntryType.Adjustment,
                amount,
                idempotency_key = entryIdempotencyKey,
            }),
        };
        var header = useUniqueSyncHeader
            ? $"entry-{Guid.NewGuid():N}"[..32]
            : $"entry-{entryIdempotencyKey}-{Guid.NewGuid():N}"[..32];
        req.Headers.Add("Idempotency-Key", header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"PUT /sync/ledger_entries with amount={amount} must succeed");
    }
}
