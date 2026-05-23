using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M12 task 10 — the field-doctor-visits log must surface each visit's services (procedure → catalog
/// service name) and medications (prescription → product name). Live dev data has field visits without
/// procedures/prescriptions, so this seeds a field visit with one of each and asserts both lines appear
/// with the right names, and that a clinic visit is excluded.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FieldDoctorVisitsReportTests
{
    [Fact]
    public async Task FieldVisit_SurfacesServicesAndMedications_AndExcludesClinicVisits()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);

        var fieldVisitId = Guid.CreateVersion7();
        await using (var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId }))
        {
            var now = DateTimeOffset.UtcNow;
            var customerId = Guid.CreateVersion7();
            db.Customers.Add(new Customer
            {
                Id = customerId, EnvironmentId = scope.EnvironmentId, Type = CustomerType.PoultryFarm,
                FullName = "Field Log Farm", CreatedAt = now, UpdatedAt = now,
            });

            var serviceId = Guid.CreateVersion7();
            db.Services.Add(new Service
            {
                Id = serviceId, EnvironmentId = scope.EnvironmentId, NameAr = "فحص ميداني",
                DefaultPrice = 40m, CreatedAt = now, UpdatedAt = now,
            });

            var productId = Guid.CreateVersion7();
            db.Products.Add(new Product
            {
                Id = productId, EnvironmentId = scope.EnvironmentId, NameAr = "مضاد حيوي",
                Category = ProductCategory.Medication, PurchasePrice = 5m, SellingPrice = 12m, CreatedAt = now, UpdatedAt = now,
            });

            db.Visits.Add(new Visit
            {
                Id = fieldVisitId, EnvironmentId = scope.EnvironmentId, VisitType = VisitType.Field,
                CustomerId = customerId, DoctorId = admin.Id, Status = VisitStatus.Completed,
                StartedAt = now, EndedAt = now, CreatedAt = now, UpdatedAt = now,
            });
            db.Procedures.Add(new Procedure
            {
                Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId, VisitId = fieldVisitId,
                ServiceId = serviceId, Price = 40m, CreatedAt = now, UpdatedAt = now,
            });
            db.Prescriptions.Add(new Prescription
            {
                Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId, VisitId = fieldVisitId,
                ProductId = productId, Dosage = "5ml", Quantity = 2m, DispenseType = DispenseType.AdministeredInClinic,
                CreatedAt = now, UpdatedAt = now,
            });

            // A clinic visit for the same doctor — must NOT appear in a field-only report.
            db.Visits.Add(new Visit
            {
                Id = Guid.CreateVersion7(), EnvironmentId = scope.EnvironmentId, VisitType = VisitType.InClinic,
                CustomerId = customerId, DoctorId = admin.Id, Status = VisitStatus.Completed,
                StartedAt = now, EndedAt = now, CreatedAt = now, UpdatedAt = now,
            });

            await db.SaveChangesAsync();
        }

        await using var factory = new VetApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueAccessToken(new UserPrincipal(admin.Id, admin.EnvironmentId, "admin")).Token);

        var resp = await client.GetAsync($"/reports/field-doctor-visits?doctorId={admin.Id}&take=200");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await resp.Content.ReadFromJsonAsync<FieldDoctorVisitsReportResponse>())!;

        report.TotalCount.Should().Be(1, "only the field visit counts, not the clinic one");
        var row = report.Rows.Should().ContainSingle().Subject;
        row.VisitId.Should().Be(fieldVisitId);

        row.Services.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ServiceName = "فحص ميداني", Price = 40m });
        row.Medications.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ProductName = "مضاد حيوي", Dosage = "5ml", Quantity = (decimal?)2m, DispenseType = DispenseType.AdministeredInClinic });
    }
}
