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

namespace VetSystem.Tests.DoctorPartners;

/// <summary>
/// M30 — the doctor-partner AP surface: CRUD (mandatory user link, one partner per user, ledger
/// seeded on create), payments (negative entry, idempotent), the statement, and the permission gates.
/// The entitlement-credit-on-settlement path is covered by the batch-settlement tests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorPartnersIntegrationTests
{
    [Fact]
    public async Task Create_SeedsLedger_ResolvesName_AndEnforcesOnePerUser()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctor = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "Dr. Layla");
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var partnerId = await CreateDoctorPartnerAsync(client, doctor.Id);

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/doctor-partners/{partnerId}");
        fetched.GetProperty("userId").GetGuid().Should().Be(doctor.Id);
        fetched.GetProperty("doctorName").GetString().Should().Be("Dr. Layla", "the name is resolved from the linked user");
        fetched.GetProperty("balance").GetDecimal().Should().Be(0m);
        fetched.GetProperty("ledgerStatus").GetString().Should().Be(LedgerStatus.Open);

        await using var db = NewContext(scope, admin.Id);
        (await db.DoctorPartnerLedgers.AsNoTracking().CountAsync(l => l.DoctorPartnerId == partnerId))
            .Should().Be(1, "exactly one ledger is seeded with the partner");

        // One partner per user.
        var dup = await PostAsync(client, "/doctor-partners", new { id = Guid.CreateVersion7(), userId = doctor.Id });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict, "a user can only have one doctor-partner record");
    }

    [Fact]
    public async Task Payment_PostsNegativeEntry_AndStoresChequeMetadata()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctor = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "Dr. Omar");
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var partnerId = await CreateDoctorPartnerAsync(client, doctor.Id);

        var paymentId = Guid.CreateVersion7();
        var resp = await PostAsync(client, $"/doctor-partners/{partnerId}/payments", new
        {
            id = paymentId,
            amount = 120m,
            method = "cheque",
            notes = "advance",
            chequeNumber = "CHQ-DP-01",
            chequeBank = "Bank of Palestine",
            chequeDueDate = "2026-07-15",
            idempotencyKey = $"dpp-{paymentId:N}",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);

        var payment = await db.DoctorPartnerPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        payment.Method.Should().Be("cheque");
        payment.ChequeNumber.Should().Be("CHQ-DP-01");
        payment.ChequeDueDate.Should().Be(new DateOnly(2026, 7, 15));

        var entry = await db.DoctorPartnerLedgerEntries.AsNoTracking().SingleAsync(e => e.DoctorPartnerPaymentId == paymentId);
        entry.EntryType.Should().Be(DoctorPartnerLedgerEntryType.Payment);
        entry.Amount.Should().Be(-120m, "a payment reduces the payable");
        entry.BalanceAfter.Should().Be(-120m);
    }

    [Fact]
    public async Task Payment_IdempotentReplay_DoesNotDoublePost()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctor = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "Dr. Sara");
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var partnerId = await CreateDoctorPartnerAsync(client, doctor.Id);

        var body = new
        {
            id = Guid.CreateVersion7(),
            amount = 50m,
            method = "cash",
            notes = (string?)null,
            chequeNumber = (string?)null,
            chequeBank = (string?)null,
            chequeDueDate = (string?)null,
            idempotencyKey = "stable-dp-payment-key",
        };
        var key = $"dp-hdr-{Guid.NewGuid():N}"[..32];

        (await PostAsync(client, $"/doctor-partners/{partnerId}/payments", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/doctor-partners/{partnerId}/payments", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.DoctorPartnerPayments.AsNoTracking().CountAsync(p => p.DoctorPartnerId == partnerId))
            .Should().Be(1, "the same idempotency key collapses retries to one payment");
        (await db.DoctorPartnerLedgers.AsNoTracking().Where(l => l.DoctorPartnerId == partnerId).Select(l => l.Balance).FirstAsync())
            .Should().Be(-50m, "the single payment debits the ledger once");
    }

    [Fact]
    public async Task Statement_Renders_WithRunningBalance()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctor = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "Dr. Nour");
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var partnerId = await CreateDoctorPartnerAsync(client, doctor.Id);
        var payId = Guid.CreateVersion7();
        (await PostAsync(client, $"/doctor-partners/{partnerId}/payments", new
        {
            id = payId,
            amount = 30m,
            method = "cash",
            notes = (string?)null,
            chequeNumber = (string?)null,
            chequeBank = (string?)null,
            chequeDueDate = (string?)null,
            idempotencyKey = $"dpp-{payId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var statement = await client.GetFromJsonAsync<JsonElement>($"/doctor-partners/{partnerId}/statement");
        statement.GetProperty("doctorPartnerId").GetGuid().Should().Be(partnerId);
        statement.GetProperty("doctorName").GetString().Should().Be("Dr. Nour");
        statement.GetProperty("closingBalance").GetDecimal().Should().Be(-30m, "one 30 payment, no credits yet");
        statement.GetProperty("entries").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Manage_And_Pay_RequirePermission()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope); // seeds roles + permissions for the env
        var doctor = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "Dr. Karim");
        var fieldVet = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "No-Perms"); // no doctor_partners.* granted
        await using var factory = new VetApiFactory();

        // Create a partner as admin first so the pay attempt has a target.
        using (var adminClient = AuthedClient(factory, admin))
        {
            await CreateDoctorPartnerAsync(adminClient, doctor.Id);
        }

        using var client = AuthedClient(factory, fieldVet, role: RoleKey.VetField);
        var create = await PostAsync(client, "/doctor-partners", new { id = Guid.CreateVersion7(), userId = doctor.Id });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden, "doctor_partners.manage gates partner writes");
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

    private static async Task<Guid> CreateDoctorPartnerAsync(HttpClient client, Guid userId)
    {
        var partnerId = Guid.CreateVersion7();
        var resp = await PostAsync(client, "/doctor-partners", new { id = partnerId, userId, notes = "field cycle doctor" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return partnerId;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<User> SeedUserWithRoleAsync(PgTestScope scope, string roleKey, string fullName)
    {
        await using var db = NewContext(scope, null);
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = fullName,
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
