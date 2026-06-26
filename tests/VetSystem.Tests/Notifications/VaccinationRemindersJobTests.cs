using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Jobs;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M11 task 18 + exit criterion — with a forced clock set to a vaccination's <c>next_due_date</c>, the
/// reminder job fires a <c>vaccination_due</c> notification to the responsible doctor. A second run on
/// the same day is idempotent (no duplicate), proving the per-day dedupe.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VaccinationRemindersJobTests
{
    [Fact]
    public async Task Fires_for_vaccination_due_today_and_is_idempotent()
    {
        // A unique far-future due date so the env-wide scan can't collide with other tests' data.
        var dueDate = new DateOnly(2031, 3, 17);
        var forcedNow = new DateTimeOffset(dueDate.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero);

        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope); // seeds roles/permissions for the env
        var doctorId = await SeedDoctorWithDueVaccinationAsync(scope, dueDate);

        await using var factory = new VetApiFactory { Clock = new FakeClock(forcedNow) };

        await RunJobAsync(factory);
        await RunJobAsync(factory); // second run same day → must not duplicate

        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var notifications = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EnvironmentId == scope.EnvironmentId
                        && n.Type == NotificationType.VaccinationDue
                        && n.UserId == doctorId)
            .ToListAsync();

        notifications.Should().HaveCount(1, "the reminder fires once on the due date and dedupes same-day re-runs");
        notifications[0].Payload.Should().Contain("VaccinationId");
    }

    [Fact]
    public async Task Fires_for_pet_only_vaccination_via_owners_assigned_doctor()
    {
        // A pet-linked vaccination with no visit and no customerId (the standalone "specific pet" path):
        // before the recipient fallback it resolved no doctor and was silently dropped.
        var dueDate = new DateOnly(2031, 5, 9);
        var forcedNow = new DateTimeOffset(dueDate.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero);

        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedPetOnlyDueVaccinationAsync(scope, dueDate);

        await using var factory = new VetApiFactory { Clock = new FakeClock(forcedNow) };
        await RunJobAsync(factory);

        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var notifications = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EnvironmentId == scope.EnvironmentId
                        && n.Type == NotificationType.VaccinationDue
                        && n.UserId == doctorId)
            .ToListAsync();

        notifications.Should().HaveCount(1, "a pet-only vaccination resolves the recipient through the pet's owner");
    }

    private static async Task RunJobAsync(VetApiFactory factory)
    {
        using var serviceScope = factory.Services.CreateScope();
        var job = serviceScope.ServiceProvider.GetRequiredService<VaccinationRemindersJob>();
        await job.RunAsync(CancellationToken.None);
    }

    private static async Task<Guid> SeedDoctorWithDueVaccinationAsync(PgTestScope scope, DateOnly dueDate)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField);

        var doctor = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Reminder Doctor",
            PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(doctor);

        var customer = new Customer
        {
            EnvironmentId = scope.EnvironmentId,
            Type = CustomerType.PoultryFarm,
            FullName = "Reminder Farm",
            AssignedDoctorId = doctor.Id,
        };
        db.Customers.Add(customer);

        db.Vaccinations.Add(new Vaccination
        {
            EnvironmentId = scope.EnvironmentId,
            CustomerId = customer.Id,
            VaccineType = "rabies",
            DateGiven = dueDate.AddYears(-1),
            NextDueDate = dueDate,
        });

        await db.SaveChangesAsync();
        return doctor.Id;
    }

    /// <summary>Seeds a doctor + a customer that owns a pet, plus a vaccination linked ONLY to the pet
    /// (no visit, no customerId). Returns the owner's assigned-doctor id (the expected recipient).</summary>
    private static async Task<Guid> SeedPetOnlyDueVaccinationAsync(PgTestScope scope, DateOnly dueDate)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetClinic);

        var doctor = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Pet Owner Doctor",
            PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"P{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(doctor);

        var customer = new Customer
        {
            EnvironmentId = scope.EnvironmentId,
            Type = CustomerType.ClinicCustomer,
            FullName = "Pet Owner",
            AssignedDoctorId = doctor.Id,
        };
        db.Customers.Add(customer);

        var pet = new Pet
        {
            EnvironmentId = scope.EnvironmentId,
            CustomerId = customer.Id,
            Name = "Rex",
        };
        db.Pets.Add(pet);

        db.Vaccinations.Add(new Vaccination
        {
            EnvironmentId = scope.EnvironmentId,
            PetId = pet.Id, // pet-only: no customerId, no visitId
            VaccineType = "rabies",
            DateGiven = dueDate.AddYears(-1),
            NextDueDate = dueDate,
        });

        await db.SaveChangesAsync();
        return doctor.Id;
    }
}
