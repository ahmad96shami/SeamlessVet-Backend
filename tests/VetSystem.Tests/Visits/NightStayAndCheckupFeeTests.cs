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

namespace VetSystem.Tests.Visits;

/// <summary>
/// M17 task 10 / reworked for M23 — night-stays (مبيت) + the in-clinic checkup fee + the
/// free-follow-up waiver, end to end through the API. M23 decoupled billing from stay-close: close
/// only records checkout + nights; the charge bills on the invoice rail, with the visit-completion
/// <b>backstop</b> posting <c>nights × rate</c> to the right (farm vs customer) ledger (M16
/// routing) for whatever never reached the till. Until billed a stay can be edited / re-closed /
/// deleted; the backstop also auto-closes a still-open stay.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NightStayAndCheckupFeeTests
{
    [Fact]
    public async Task NightStay_CloseRecordsOnly_CompletionBackstopBillsFarmLedger()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // Configure the medical-stay per-night cost.
        (await PatchAsync(client, "/admin/settings", new { nightStayRateMedical = 50m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var customerId = await CreateCustomerAsync(client);
        var farmId = await CreateFarmAsync(client, customerId);

        // An in-clinic boarding episode attributed to the farm.
        var visitId = await CreateInClinicVisitAsync(client, customerId, admin.Id, farmId);

        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new
        {
            id = stayId,
            visitId,
            careType = "medical",
            checkInAt = "2026-06-01T06:00:00Z",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Check out two days later, before noon → 2 nights (hotel rule).
        var closeBody = await (await PostAsync(client, $"/night-stays/{stayId}/close", new
        {
            checkOutAt = "2026-06-03T06:00:00Z",
        })).Content.ReadFromJsonAsync<JsonElement>();

        closeBody.GetProperty("nightsCount").GetInt32().Should().Be(2);
        closeBody.GetProperty("total").GetDecimal().Should().Be(100m);

        await using (var db = NewContext(scope, admin.Id))
        {
            // M23 — closing records the checkout; nothing bills until the invoice rail or the backstop.
            (await db.LedgerEntries.AsNoTracking().CountAsync(e => e.EntryType == LedgerEntryType.NightStay))
                .Should().Be(0, "close no longer posts the charge");
        }

        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var farmBalance = await db.Ledgers.AsNoTracking()
                .Where(l => l.FarmId == farmId).Select(l => l.Balance).SingleAsync();
            farmBalance.Should().Be(100m, "the completion backstop routes the boarding charge to the farm ledger");

            var entry = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.EntryType == LedgerEntryType.NightStay)
                .SingleAsync();
            entry.Amount.Should().Be(100m);
            var ownBalance = await db.Ledgers.AsNoTracking()
                .Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync();
            ownBalance.Should().Be(0m, "nothing posts to the customer's own ledger for a farm-scoped stay");
        }
    }

    [Fact]
    public async Task NightStay_ReCloseRecomputes_BackstopPostsOnce_ThenBilledStayFreezes()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        (await PatchAsync(client, "/admin/settings", new { nightStayRateHotel = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var customerId = await CreateCustomerAsync(client);
        var visitId = await CreateInClinicVisitAsync(client, customerId, admin.Id, farmId: null);

        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new
        {
            id = stayId, visitId, careType = "hotel", checkInAt = "2026-06-01T06:00:00Z",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-02T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK); // 1 night × 30

        // A checkout-less close replay stays an idempotent no-op…
        var replay = await (await PostAsync(client, $"/night-stays/{stayId}/close", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        replay.GetProperty("nightsCount").GetInt32().Should().Be(1);

        // …but an explicit re-close of the unbilled stay deliberately recomputes (M23: the
        // "animal died on night one of three" correction the old freeze made impossible).
        var reclosed = await (await PostAsync(client, $"/night-stays/{stayId}/close", new
        {
            checkOutAt = "2026-06-05T06:00:00Z",
        })).Content.ReadFromJsonAsync<JsonElement>();
        reclosed.GetProperty("nightsCount").GetInt32().Should().Be(4);
        reclosed.GetProperty("total").GetDecimal().Should().Be(120m);

        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            var ownBalance = await db.Ledgers.AsNoTracking()
                .Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync();
            ownBalance.Should().Be(120m, "4 nights × 30, posted once by the backstop");
            (await db.LedgerEntries.AsNoTracking().CountAsync(e => e.EntryType == LedgerEntryType.NightStay))
                .Should().Be(1);
        }

        // Billed (backstop ledger key) → the stay is frozen on every write path.
        var patchBilled = await PatchAsync(client, $"/night-stays/{stayId}", new { nightlyRate = 99m });
        patchBilled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(patchBilled)).Should().Be("night_stay_billed");
        var deleteBilled = await DeleteAsync(client, $"/night-stays/{stayId}");
        deleteBilled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(deleteBilled)).Should().Be("night_stay_billed");
        var recloseBilled = await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-06T06:00:00Z" });
        recloseBilled.StatusCode.Should().Be(HttpStatusCode.Conflict, "re-closing a billed stay would desync the posted charge");
        (await ErrorCodeAsync(recloseBilled)).Should().Be("night_stay_billed");
    }

    [Fact]
    public async Task NightStay_ClosedUnbilled_EditRecomputes_AndDeleteRemovesTheCharge()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        (await PatchAsync(client, "/admin/settings", new { nightStayRateMedical = 50m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var customerId = await CreateCustomerAsync(client);
        var visitId = await CreateInClinicVisitAsync(client, customerId, admin.Id, farmId: null);

        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new
        {
            id = stayId, visitId, careType = "medical", checkInAt = "2026-06-01T06:00:00Z",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/night-stays/{stayId}/close", new { checkOutAt = "2026-06-03T06:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.OK); // 2 nights × 50

        // M23 — a closed-unbilled stay stays editable; a rate edit recomputes the total.
        var patched = await (await PatchAsync(client, $"/night-stays/{stayId}", new { nightlyRate = 40m }))
            .Content.ReadFromJsonAsync<JsonElement>();
        patched.GetProperty("total").GetDecimal().Should().Be(80m, "2 nights × the new 40 rate");

        // …and deletable: completing the visit afterwards finds nothing to bill.
        (await DeleteAsync(client, $"/night-stays/{stayId}")).IsSuccessStatusCode.Should().BeTrue();
        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.LedgerEntries.AsNoTracking().CountAsync(e => e.EntryType == LedgerEntryType.NightStay))
            .Should().Be(0, "the deleted stay never bills");
    }

    [Fact]
    public async Task NightStay_OpenAtCompletion_AutoClosesAndBackstopPosts()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        (await PatchAsync(client, "/admin/settings", new { nightStayRateIcu = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var customerId = await CreateCustomerAsync(client);
        var visitId = await CreateInClinicVisitAsync(client, customerId, admin.Id, farmId: null);

        var stayId = Guid.CreateVersion7();
        (await PostAsync(client, "/night-stays", new
        {
            id = stayId,
            visitId,
            careType = "icu",
            checkInAt = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"), // ≥ 2 hotel-rule nights by now
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The stay is never closed by staff — completing the visit auto-closes it (checkout = now)
        // and the backstop posts the charge.
        (await PostAsync(client, $"/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stay = await client.GetFromJsonAsync<JsonElement>($"/night-stays/{stayId}");
        stay.GetProperty("checkOutAt").ValueKind.Should().NotBe(JsonValueKind.Null, "completion auto-closes open stays");
        stay.GetProperty("nightsCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        await using var db = NewContext(scope, admin.Id);
        var entry = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryType == LedgerEntryType.NightStay)
            .SingleAsync();
        entry.Amount.Should().BeGreaterThanOrEqualTo(60m, "≥ 2 nights × 30");
    }

    [Fact]
    public async Task CheckupFee_AutoApplied_PostsOnCompletion_AndWaivedOncePerFollowUpOrigin()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        (await PatchAsync(client, "/admin/settings", new { defaultCheckupFee = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var customerId = await CreateCustomerAsync(client);

        // A new in-clinic visit auto-applies the default checkup fee (editable).
        var originVisitId = await CreateInClinicVisitAsync(client, customerId, admin.Id, farmId: null);
        var origin = await client.GetFromJsonAsync<JsonElement>($"/visits/{originVisitId}");
        origin.GetProperty("checkupFeeApplied").GetDecimal().Should().Be(30m);

        // Completing it posts the checkup fee to the customer ledger.
        (await PostAsync(client, $"/visits/{originVisitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync())
                .Should().Be(30m);
            (await db.LedgerEntries.AsNoTracking().CountAsync(e => e.EntryType == LedgerEntryType.CheckupFee))
                .Should().Be(1);
        }

        // The first follow-up of this origin waives the checkup fee.
        var firstFollowVisit = await ScheduleAndAttendFollowUpAsync(client, originVisitId, admin.Id, "2026-07-01T09:00:00Z");
        var firstVisit = await client.GetFromJsonAsync<JsonElement>($"/visits/{firstFollowVisit}");
        firstVisit.GetProperty("checkupFeeApplied").GetDecimal().Should().Be(0m, "the one free follow-up waives the fee");
        firstVisit.GetProperty("followUpOfVisitId").GetGuid().Should().Be(originVisitId);

        // Completing the waived follow-up posts no checkup fee.
        (await PostAsync(client, $"/visits/{firstFollowVisit}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = NewContext(scope, admin.Id))
        {
            (await db.Ledgers.AsNoTracking().Where(l => l.CustomerId == customerId).Select(l => l.Balance).SingleAsync())
                .Should().Be(30m, "the waived follow-up adds nothing");
        }

        // A second follow-up of the same origin is charged the normal fee (free one used).
        var secondFollowVisit = await ScheduleAndAttendFollowUpAsync(client, originVisitId, admin.Id, "2026-07-02T09:00:00Z");
        var secondVisit = await client.GetFromJsonAsync<JsonElement>($"/visits/{secondFollowVisit}");
        secondVisit.GetProperty("checkupFeeApplied").GetDecimal().Should().Be(30m, "only one free follow-up per origin");
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

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path)
    {
        // Bare HttpClient.DeleteAsync would 409 on idempotency_key_required, not the guard under test.
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId,
            type = "clinic_customer",
            fullName = "Night Stay Co",
            phonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return customerId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client, Guid customerId)
    {
        var farmId = Guid.CreateVersion7();
        (await PostAsync(client, "/farms", new
        {
            id = farmId,
            customerId,
            name = $"Farm {farmId:N}"[..16],
            kind = "poultry",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return farmId;
    }

    private static async Task<Guid> CreateInClinicVisitAsync(
        HttpClient client, Guid customerId, Guid doctorId, Guid? farmId, decimal? checkupFee = null)
    {
        var visitId = Guid.CreateVersion7();
        // checkupFee null → the server auto-applies the settings default; an explicit value overrides it.
        (await PostAsync(client, "/visits", new
        {
            id = visitId,
            visitType = "in_clinic",
            customerId,
            farmId,
            doctorId,
            status = "in_progress",
            checkupFeeApplied = checkupFee,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return visitId;
    }

    private static async Task<Guid> ScheduleAndAttendFollowUpAsync(
        HttpClient client, Guid originVisitId, Guid doctorId, string scheduledAt)
    {
        var followAppt = await (await PostAsync(client, $"/visits/{originVisitId}/schedule-follow-up", new
        {
            id = Guid.CreateVersion7(),
            scheduledAt,
            doctorId,
            durationMin = 30,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var appointmentId = followAppt.GetProperty("id").GetGuid();

        (await PostAsync(client, $"/appointments/{appointmentId}/attend", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var appt = await client.GetFromJsonAsync<JsonElement>($"/appointments/{appointmentId}");
        appt.GetProperty("isFollowUp").GetBoolean().Should().BeTrue();
        return appt.GetProperty("visitId").GetGuid();
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
