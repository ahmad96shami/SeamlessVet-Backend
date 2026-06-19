using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Visits;

/// <summary>
/// When a visit is created and assigned to a doctor by a <b>different</b> user (the receptionist
/// flow), the assigned doctor gets a <c>visit_assigned</c> in-app notification. A doctor who creates
/// their own visit is not notified (creator == assignee).
/// </summary>
[Trait("Category", "Integration")]
public sealed class VisitAssignedNotificationTests
{
    [Fact]
    public async Task PostVisits_AssigningAnotherDoctor_NotifiesThatDoctor_ButNotOnSelfAssign()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var doctor = await SeedClinicDoctorAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Notify Owner", phonePrimary = "+970590007654",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin (acting as receptionist) assigns the OTHER doctor → that doctor should be notified.
        var assignedVisitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = assignedVisitId, visitType = "in_clinic", customerId, doctorId = doctor.Id, status = "open",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin assigns the visit to themselves → no self-notification.
        var selfVisitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = selfVisitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "open",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true, EnvironmentId = scope.EnvironmentId, UserId = admin.Id,
        });

        var doctorNotes = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == doctor.Id && n.Type == NotificationType.VisitAssigned)
            .ToListAsync();

        doctorNotes.Should().ContainSingle("the assigned doctor is notified exactly once");
        doctorNotes[0].Title.Should().NotBeNullOrWhiteSpace();
        doctorNotes[0].Payload.Should().Contain(assignedVisitId.ToString(),
            "the payload carries the visit id so the client can deep-link to it");

        (await db.Notifications.AsNoTracking()
            .AnyAsync(n => n.UserId == admin.Id && n.Type == NotificationType.VisitAssigned))
            .Should().BeFalse("a doctor who creates their own visit is not notified");
    }

    private static async Task<User> SeedClinicDoctorAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var clinicRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetClinic);

        var doctor = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = clinicRole.Id,
            FullName = "Clinic Doctor",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = new BCryptPasswordHasher().Hash("DoctorTest_pw_1!"),
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(doctor);
        await db.SaveChangesAsync();
        return doctor;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }
}
