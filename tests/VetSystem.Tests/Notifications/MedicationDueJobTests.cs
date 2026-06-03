using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Jobs;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M18 task 8 + exit criterion — with a forced clock, <see cref="MedicationDueJob"/> fires a
/// <c>medication_due</c> notification at each dose's reminder instant (<c>dose − lead</c>) and never
/// before; a same-clock re-run is idempotent (the dose high-water mark), and advancing the clock to the
/// next dose window fires the next reminder. The notification reaches the visit's doctor.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MedicationDueJobTests
{
    [Fact]
    public async Task Fires_each_dose_window_with_lead_time_and_is_idempotent()
    {
        // Far-future schedule so the env-wide scan can't collide with other tests' data.
        // Dose 0 at 09:00 (reminder 08:30 with a 30-minute lead), dose 1 at 15:00 (reminder 14:30).
        var start = new DateTimeOffset(2031, 5, 12, 9, 0, 0, TimeSpan.Zero);
        const int intervalMinutes = 360; // 6 hours
        const int leadMinutes = 30;

        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var (doctorId, _) = await SeedDoctorWithReminderPrescriptionAsync(scope, start, intervalMinutes, leadMinutes);

        var clock = new FakeClock(start.AddMinutes(-leadMinutes - 1)); // one minute before dose 0's reminder
        await using var factory = new VetApiFactory { Clock = clock };

        await RunJobAsync(factory);
        (await DueNotificationsAsync(scope, doctorId)).Should().BeEmpty("no reminder is due before dose 0's lead window");

        clock.UtcNow = start.AddMinutes(-leadMinutes); // exactly dose 0's reminder instant
        await RunJobAsync(factory);
        await RunJobAsync(factory); // same clock → must not duplicate (high-water mark)
        var afterDose0 = await DueNotificationsAsync(scope, doctorId);
        afterDose0.Should().HaveCount(1, "dose 0's reminder fires once at its lead instant");
        DoseNumberOf(afterDose0[0]).Should().Be(0);

        clock.UtcNow = start.AddMinutes(intervalMinutes - leadMinutes); // dose 1's reminder instant
        await RunJobAsync(factory);
        var afterDose1 = await DueNotificationsAsync(scope, doctorId);
        afterDose1.Should().HaveCount(2, "advancing to the next dose window fires the next reminder");
        afterDose1.Select(DoseNumberOf).Should().BeEquivalentTo(new[] { 0, 1 });
    }

    private static async Task RunJobAsync(VetApiFactory factory)
    {
        using var serviceScope = factory.Services.CreateScope();
        var job = serviceScope.ServiceProvider.GetRequiredService<MedicationDueJob>();
        await job.RunAsync(CancellationToken.None);
    }

    private static async Task<List<Notification>> DueNotificationsAsync(PgTestScope scope, Guid doctorId)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        return await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EnvironmentId == scope.EnvironmentId
                        && n.Type == NotificationType.MedicationDue
                        && n.UserId == doctorId)
            .ToListAsync();
    }

    private static int DoseNumberOf(Notification n)
    {
        using var doc = JsonDocument.Parse(n.Payload!);
        return doc.RootElement.GetProperty("DoseNumber").GetInt32();
    }

    private static async Task<(Guid DoctorId, Guid PrescriptionId)> SeedDoctorWithReminderPrescriptionAsync(
        PgTestScope scope, DateTimeOffset start, int intervalMinutes, int leadMinutes)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var now = DateTimeOffset.UtcNow;

        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField);

        var doctor = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Medication Doctor",
            PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"M{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(doctor);

        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            Type = CustomerType.Home,
            FullName = "Medication Owner",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Customers.Add(customer);

        var productId = Guid.CreateVersion7();
        db.Products.Add(new Product
        {
            Id = productId,
            EnvironmentId = scope.EnvironmentId,
            NameAr = "مضاد حيوي",
            Category = ProductCategory.Medication,
            PurchasePrice = 5m,
            SellingPrice = 12m,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var visitId = Guid.CreateVersion7();
        db.Visits.Add(new Visit
        {
            Id = visitId,
            EnvironmentId = scope.EnvironmentId,
            VisitType = VisitType.InClinic,
            CustomerId = customer.Id,
            DoctorId = doctor.Id,
            Status = VisitStatus.Completed,
            StartedAt = now,
            EndedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var prescriptionId = Guid.CreateVersion7();
        db.Prescriptions.Add(new Prescription
        {
            Id = prescriptionId,
            EnvironmentId = scope.EnvironmentId,
            VisitId = visitId,
            ProductId = productId,
            Dosage = "1 tablet",
            Quantity = 10m,
            DispenseType = DispenseType.DispensedToOwner,
            ReminderEnabled = true,
            IntervalMinutes = intervalMinutes,
            LeadMinutes = leadMinutes,
            StartAt = start,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (doctor.Id, prescriptionId);
    }
}
