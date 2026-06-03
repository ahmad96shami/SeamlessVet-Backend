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

namespace VetSystem.Tests.Vaccinations;

/// <summary>
/// M18 tasks 5+6 — a vaccination created standalone (no <c>visit_id</c>, just a farm-group customer)
/// is accepted by <c>POST /vaccinations</c> and surfaces in the <c>GET /vaccinations/upcoming</c>
/// calendar query for its due range.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StandaloneVaccinationUpcomingTests
{
    [Fact]
    public async Task Standalone_vaccination_is_created_without_a_visit_and_appears_in_upcoming()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        var customerId = Guid.CreateVersion7();
        await using (var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId }))
        {
            var now = DateTimeOffset.UtcNow;
            db.Customers.Add(new Customer
            {
                Id = customerId, EnvironmentId = scope.EnvironmentId, Type = CustomerType.PoultryFarm,
                FullName = "Standalone Vacc Farm", CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var vaccinationId = Guid.CreateVersion7();
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(21);

        var create = new HttpRequestMessage(HttpMethod.Post, "/vaccinations")
        {
            Content = JsonContent.Create(new
            {
                id = vaccinationId,
                customerId, // standalone: no petId, no visitId
                vaccineType = "newcastle",
                dateGiven = DateOnly.FromDateTime(DateTime.UtcNow),
                nextDueDate = dueDate,
            }),
        };
        create.Headers.Add("Idempotency-Key", $"vacc-{Guid.NewGuid():N}"[..32]);
        var createResp = await client.SendAsync(create);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, "a vaccination needs no visit — a pet or customer is enough");

        // It carries no visit yet still persisted.
        await using (var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId }))
        {
            var row = await db.Vaccinations.IgnoreQueryFilters().FirstAsync(v => v.Id == vaccinationId);
            row.VisitId.Should().BeNull();
            row.CustomerId.Should().Be(customerId);
        }

        var upcomingResp = await client.GetAsync(
            $"/vaccinations/upcoming?from={dueDate.AddDays(-7):yyyy-MM-dd}&to={dueDate.AddDays(7):yyyy-MM-dd}");
        upcomingResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await upcomingResp.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
        ids.Should().Contain(vaccinationId, "the standalone vaccination is due within the requested range");
    }
}
