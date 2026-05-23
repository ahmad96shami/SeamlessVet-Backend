using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Appointments;

/// <summary>
/// M6 task 11 + exit criteria — end-to-end against the PG test container:
/// (1) booking two overlapping slots for one doctor returns a conflict and the second is not
/// persisted (back-to-back is allowed, proving half-open semantics at the DB level); and
/// (2) attending an appointment opens a linked clinic visit that surfaces in the pet's M5 timeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppointmentLifecycleIntegrationTests
{
    private static readonly DateTimeOffset Slot = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OverlappingBooking_IsRejected_AndNotPersisted()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthClient(factory, admin);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Appt Owner", phonePrimary = "+970590004321",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // 09:00–09:30 booked for the admin/doctor.
        (await PostAsync(client, "/appointments", new
        {
            id = Guid.CreateVersion7(), customerId, doctorId = admin.Id, scheduledAt = Slot, durationMin = 30,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // 09:15–09:45 overlaps by 15 minutes → rejected.
        var overlap = await PostAsync(client, "/appointments", new
        {
            id = Guid.CreateVersion7(), customerId, doctorId = admin.Id,
            scheduledAt = Slot.AddMinutes(15), durationMin = 30,
        });
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await overlap.Content.ReadAsStringAsync()).Should().Contain("appointment_conflict");

        // 09:30–10:00 is back-to-back (touching boundary) → allowed under half-open semantics.
        (await PostAsync(client, "/appointments", new
        {
            id = Guid.CreateVersion7(), customerId, doctorId = admin.Id,
            scheduledAt = Slot.AddMinutes(30), durationMin = 30,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Exactly the two valid bookings persisted; the overlapping one was not.
        var listed = await GetAsync<List<AppointmentDto>>(client, $"/appointments?doctorId={admin.Id}");
        listed.Should().HaveCount(2);
    }

    [Fact]
    public async Task Attend_OpensLinkedVisit_ThatAppearsInPetTimeline()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthClient(factory, admin);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Timeline Owner", phonePrimary = "+970590009876",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var petId = Guid.CreateVersion7();
        (await PostAsync(client, "/pets", new
        {
            id = petId, customerId, name = "Rex", species = "dog",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var appointmentId = Guid.CreateVersion7();
        (await PostAsync(client, "/appointments", new
        {
            id = appointmentId, customerId, petId, doctorId = admin.Id, scheduledAt = Slot, durationMin = 30,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/appointments/{appointmentId}/attend", new { })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // The appointment is now attended and carries the id of the visit it opened.
        var attended = await GetAsync<AppointmentDto>(client, $"/appointments/{appointmentId}");
        attended.Status.Should().Be(AppointmentStatus.Attended);
        attended.VisitId.Should().NotBeNull();

        // …and that visit shows up in the pet's medical timeline as an open clinic visit.
        var timeline = await GetAsync<JsonElement>(client, $"/pets/{petId}/timeline");
        var visits = timeline.GetProperty("visits").EnumerateArray().ToList();
        visits.Should().ContainSingle();
        visits[0].GetProperty("visitId").GetGuid().Should().Be(attended.VisitId!.Value);
        visits[0].GetProperty("visitType").GetString().Should().Be(VisitType.InClinic);
        visits[0].GetProperty("status").GetString().Should().Be(VisitStatus.Open);
    }

    private static HttpClient AuthClient(VetApiFactory factory, User admin)
    {
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", jwt.IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0} should succeed", path);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record AppointmentDto(Guid Id, string Status, Guid? VisitId);
}
