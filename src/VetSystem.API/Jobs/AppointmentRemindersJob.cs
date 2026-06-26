using Microsoft.EntityFrameworkCore;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Application.Settings;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// Intraday scan of upcoming appointments (PRD §5.3, §9). For every still-active appointment
/// (<c>scheduled</c>/<c>confirmed</c>) whose <c>scheduled_at</c> falls within the configured lead
/// window (<c>appointmentReminder.leadMinutes</c> in <c>system_settings.extra</c>), notifies the
/// responsible doctor (the appointment's doctor, else the customer's assigned doctor, else the
/// environment's admins so nothing is dropped). Exactly-once per appointment: an appointment already
/// reminded today is skipped (dedupe by the <c>AppointmentId</c> in the notification payload), so the
/// 15-minute cadence never double-sends. Mirrors <see cref="VaccinationRemindersJob"/> /
/// <see cref="MedicationDueJob"/>; driven by <see cref="IClock"/> for deterministic tests.
/// </summary>
public sealed class AppointmentRemindersJob
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationRecipientResolver _recipients;
    private readonly IClock _clock;

    public AppointmentRemindersJob(
        ApplicationDbContext db,
        INotificationDispatcher dispatcher,
        NotificationRecipientResolver recipients,
        IClock clock)
    {
        _db = db;
        _dispatcher = dispatcher;
        _recipients = recipients;
        _clock = clock;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var todayStart = JobHelpers.StartOfTodayUtc(_clock);

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            var leadMinutes = await LeadMinutesAsync(environmentId, cancellationToken);
            var windowEnd = now.AddMinutes(leadMinutes);

            // Upcoming appointments that fall inside the lead window (future, within `lead` of now) and
            // still occupy the slot (cancelled / no-show / attended are excluded).
            var upcoming = await _db.Appointments
                .IgnoreQueryFilters()
                .Where(a => a.EnvironmentId == environmentId
                            && a.DeletedAt == null
                            && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
                            && a.ScheduledAt > now
                            && a.ScheduledAt <= windowEnd)
                .Select(a => new { a.Id, a.DoctorId, a.CustomerId, a.ScheduledAt })
                .ToListAsync(cancellationToken);

            if (upcoming.Count == 0)
            {
                continue;
            }

            var notifiedPayloads = await _db.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.EnvironmentId == environmentId
                            && n.Type == NotificationType.AppointmentReminder
                            && n.CreatedAt >= todayStart)
                .Select(n => n.Payload)
                .ToListAsync(cancellationToken);

            var alreadyNotified = notifiedPayloads
                .Select(p => JobHelpers.TryReadPayloadGuid(p, "AppointmentId"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            foreach (var appt in upcoming)
            {
                if (alreadyNotified.Contains(appt.Id))
                {
                    continue;
                }

                Guid? doctorId = appt.DoctorId;
                if (doctorId is null && appt.CustomerId is { } customerId)
                {
                    doctorId = await _db.Customers
                        .IgnoreQueryFilters()
                        .Where(c => c.Id == customerId)
                        .Select(c => c.AssignedDoctorId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                IReadOnlyCollection<Guid> recipients = doctorId is { } recipient
                    ? [recipient]
                    : await _recipients.AdminsAsync(environmentId, cancellationToken);

                if (recipients.Count == 0)
                {
                    continue;
                }

                await _dispatcher.DispatchAsync(
                    new NotificationDispatch(
                        environmentId,
                        recipients,
                        NotificationType.AppointmentReminder,
                        Title: "موعد قادم",
                        Body: $"لديك موعد بتاريخ {appt.ScheduledAt:yyyy-MM-dd HH:mm}.",
                        Payload: new
                        {
                            AppointmentId = appt.Id,
                            appt.DoctorId,
                            appt.CustomerId,
                            appt.ScheduledAt,
                        }),
                    cancellationToken);
            }
        }
    }

    /// <summary>The configured "fire X minutes before" lead (<c>appointmentReminder.leadMinutes</c>).</summary>
    private async Task<int> LeadMinutesAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var extra = await _db.SystemSettings
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => s.Extra)
            .FirstOrDefaultAsync(cancellationToken);

        return AppointmentReminderSettings.FromExtra(extra).LeadMinutes;
    }
}
