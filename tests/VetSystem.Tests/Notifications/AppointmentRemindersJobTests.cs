using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Jobs;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// With a forced clock placed shortly before an appointment, the reminder job fires an
/// <c>appointment_reminder</c> notification to the appointment's doctor when the booking falls inside
/// the (default 60-minute) lead window, and dedupes same-day re-runs. An appointment further out than
/// the lead window does not fire yet.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppointmentRemindersJobTests
{
    [Fact]
    public async Task Fires_for_appointment_inside_lead_window_and_is_idempotent()
    {
        // Appointment at 10:00; default lead is 60 min, so a 09:30 clock is inside the window.
        var scheduledAt = new DateTimeOffset(2031, 6, 12, 10, 0, 0, TimeSpan.Zero);
        var forcedNow = scheduledAt.AddMinutes(-30);

        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedDoctorWithAppointmentAsync(scope, scheduledAt);

        await using var factory = new VetApiFactory { Clock = new FakeClock(forcedNow) };
        await RunJobAsync(factory);
        await RunJobAsync(factory); // same-day re-run → must not duplicate

        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var notifications = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EnvironmentId == scope.EnvironmentId
                        && n.Type == NotificationType.AppointmentReminder
                        && n.UserId == doctorId)
            .ToListAsync();

        notifications.Should().HaveCount(1, "the reminder fires once inside the lead window and dedupes same-day re-runs");
        notifications[0].Payload.Should().Contain("AppointmentId");
    }

    [Fact]
    public async Task Does_not_fire_when_appointment_is_beyond_lead_window()
    {
        // Appointment 3 hours out with the default 60-minute lead → outside the window, no reminder.
        var scheduledAt = new DateTimeOffset(2031, 7, 4, 14, 0, 0, TimeSpan.Zero);
        var forcedNow = scheduledAt.AddHours(-3);

        await using var scope = await PgTestScope.CreateAsync();
        await AdminTestSeed.SeedAdminAsync(scope);
        var doctorId = await SeedDoctorWithAppointmentAsync(scope, scheduledAt);

        await using var factory = new VetApiFactory { Clock = new FakeClock(forcedNow) };
        await RunJobAsync(factory);

        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var count = await db.Notifications
            .IgnoreQueryFilters()
            .CountAsync(n => n.EnvironmentId == scope.EnvironmentId
                             && n.Type == NotificationType.AppointmentReminder
                             && n.UserId == doctorId);

        count.Should().Be(0, "the appointment is outside the configured lead window");
    }

    private static async Task RunJobAsync(VetApiFactory factory)
    {
        using var serviceScope = factory.Services.CreateScope();
        var job = serviceScope.ServiceProvider.GetRequiredService<AppointmentRemindersJob>();
        await job.RunAsync(CancellationToken.None);
    }

    private static async Task<Guid> SeedDoctorWithAppointmentAsync(PgTestScope scope, DateTimeOffset scheduledAt)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetClinic);

        var doctor = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Appointment Doctor",
            PhonePrimary = $"+9705{Guid.NewGuid().ToString("N")[..8]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"A{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(doctor);

        db.Appointments.Add(new Appointment
        {
            EnvironmentId = scope.EnvironmentId,
            DoctorId = doctor.Id,
            ScheduledAt = scheduledAt,
            Status = AppointmentStatus.Scheduled,
        });

        await db.SaveChangesAsync();
        return doctor.Id;
    }
}
