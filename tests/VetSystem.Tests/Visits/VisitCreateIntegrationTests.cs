using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Visits;

/// <summary>
/// M13 task 5 — visit creation end-to-end for both visit types. <c>POST /visits</c> persists an
/// in-clinic and a field visit for the same customer, each with its declared type and status, scoped
/// to the caller's environment, and each is then retrievable via <c>GET /visits/{id}</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VisitCreateIntegrationTests
{
    [Fact]
    public async Task PostVisits_CreatesClinicAndField_EndToEnd()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var customerId = Guid.CreateVersion7();
        (await PostAsync(client, "/customers", new
        {
            id = customerId, type = "home", fullName = "Visit Owner", phonePrimary = "+970590004321",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var clinicVisitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = clinicVisitId, visitType = "in_clinic", customerId, doctorId = admin.Id, status = "open",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var fieldVisitId = Guid.CreateVersion7();
        (await PostAsync(client, "/visits", new
        {
            id = fieldVisitId, visitType = "field", customerId, doctorId = admin.Id, status = "open",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true, EnvironmentId = scope.EnvironmentId, UserId = admin.Id,
        }))
        {
            var visits = await db.Visits.AsNoTracking().Where(v => v.CustomerId == customerId).ToListAsync();
            visits.Should().HaveCount(2);
            visits.Should().ContainSingle(v => v.Id == clinicVisitId && v.VisitType == VisitType.InClinic);
            visits.Should().ContainSingle(v => v.Id == fieldVisitId && v.VisitType == VisitType.Field);
        }

        foreach (var id in new[] { clinicVisitId, fieldVisitId })
        {
            (await client.GetAsync($"/visits/{id}")).StatusCode.Should().Be(HttpStatusCode.OK, $"visit {id} should be retrievable");
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }
}
