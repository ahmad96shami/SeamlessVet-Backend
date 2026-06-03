using Microsoft.EntityFrameworkCore;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Application.Settings;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M18 task 4 — recurring medication-due reminders for dispensed drugs (PRD §6.7, §9). For every
/// prescription with <see cref="Prescription.ReminderEnabled"/>, doses fall at
/// <c>start_at + k·interval_minutes</c> (k = 0,1,2,…) bounded by whichever of <c>doses_count</c> /
/// <c>end_at</c> is set; a reminder is due <c>lead_minutes</c> ahead of each dose (the prescription's
/// own lead, else the per-environment <c>medicationReminder.defaultLeadMinutes</c>). The notification
/// goes to the visit's doctor (the prescriber) plus the clinic vets.
/// <para>
/// Each run fires the <b>current</b> due dose only — the latest dose whose reminder instant has passed —
/// so a coarser run cadence or a gap simply coalesces to "a dose is due now" rather than flooding stale
/// reminders. Exactly-once per dose is held by the server-managed
/// <see cref="Prescription.LastRemindedDose"/> high-water mark, which is advanced in the same
/// <c>SaveChanges</c> as the notification rows (the job and the dispatcher share one scoped DbContext),
/// so a re-run never double-sends. Driven by <see cref="IClock"/> so a forced clock tests it
/// deterministically; runs every few minutes (UTC cron) since doses are intraday — unlike the daily
/// vaccination scan it mirrors.
/// </para>
/// </summary>
public sealed class MedicationDueJob
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationRecipientResolver _recipients;
    private readonly IClock _clock;

    public MedicationDueJob(
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

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            // Tracked (not projected) so advancing the high-water mark below flushes atomically with the
            // notification rows the dispatcher writes on the shared DbContext.
            var active = await _db.Prescriptions
                .IgnoreQueryFilters()
                .Where(p => p.EnvironmentId == environmentId
                            && p.DeletedAt == null
                            && p.ReminderEnabled
                            && p.IntervalMinutes != null
                            && p.StartAt != null)
                .ToListAsync(cancellationToken);

            if (active.Count == 0)
            {
                continue;
            }

            var defaultLead = await DefaultLeadMinutesAsync(environmentId, cancellationToken);
            var clinicStaff = await _recipients.ByRolesAsync(
                environmentId, [RoleKey.VetClinic, RoleKey.VetBoth], cancellationToken);

            foreach (var rx in active)
            {
                var interval = rx.IntervalMinutes!.Value;
                if (interval <= 0)
                {
                    continue;
                }

                var lead = rx.LeadMinutes ?? defaultLead;
                var dueDose = CurrentDueDose(rx.StartAt!.Value, interval, lead, rx.EndAt, rx.DosesCount, now);
                if (dueDose is not { } k || k <= (rx.LastRemindedDose ?? -1))
                {
                    continue; // not started / no valid dose yet, or already reminded up to here
                }

                var doctorId = await _db.Visits
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == rx.VisitId)
                    .Select(v => (Guid?)v.DoctorId)
                    .FirstOrDefaultAsync(cancellationToken);

                var recipients = new List<Guid>(clinicStaff);
                if (doctorId is { } d)
                {
                    recipients.Add(d);
                }

                if (recipients.Count == 0)
                {
                    continue; // no one to notify — leave the mark so a later run retries when staff exist
                }

                var doseAt = rx.StartAt!.Value.AddMinutes((double)k * interval);

                // Advance the mark on the tracked entity first; the dispatcher's SaveChanges commits it
                // together with the notification rows (one transaction → exactly-once).
                rx.LastRemindedDose = k;

                await _dispatcher.DispatchAsync(
                    new NotificationDispatch(
                        environmentId,
                        recipients,
                        NotificationType.MedicationDue,
                        Title: "موعد جرعة دواء",
                        Body: $"حان موعد إعطاء الدواء (الجرعة رقم {k + 1}) بتاريخ {doseAt:yyyy-MM-dd HH:mm}.",
                        Payload: new
                        {
                            PrescriptionId = rx.Id,
                            rx.VisitId,
                            rx.ProductId,
                            DoseNumber = k,
                            DoseAt = doseAt,
                        }),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// The largest dose index whose reminder instant (<c>start + k·interval − lead</c>) is at or before
    /// <paramref name="now"/>, clamped to the schedule's bounds. <c>null</c> when the first reminder has
    /// not arrived yet or the schedule has no valid dose.
    /// </summary>
    private static int? CurrentDueDose(
        DateTimeOffset start, int intervalMinutes, int leadMinutes,
        DateTimeOffset? endAt, int? dosesCount, DateTimeOffset now)
    {
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var lead = TimeSpan.FromMinutes(leadMinutes);

        var elapsed = now - (start - lead);
        if (elapsed < TimeSpan.Zero)
        {
            return null; // even dose 0's reminder hasn't come due
        }

        long k = elapsed.Ticks / interval.Ticks; // integer floor (elapsed ≥ 0)

        long lastValid = long.MaxValue;
        if (dosesCount is { } dc)
        {
            lastValid = Math.Min(lastValid, dc - 1L);
        }
        if (endAt is { } e)
        {
            if (e < start)
            {
                return null;
            }
            lastValid = Math.Min(lastValid, (e - start).Ticks / interval.Ticks);
        }

        if (lastValid < 0)
        {
            return null;
        }
        if (k > lastValid)
        {
            k = lastValid; // past the end → the final dose is the current one
        }

        return (int)Math.Min(k, int.MaxValue);
    }

    private async Task<int> DefaultLeadMinutesAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var extra = await _db.SystemSettings
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => s.Extra)
            .FirstOrDefaultAsync(cancellationToken);

        return MedicationReminderSettings.FromExtra(extra).DefaultLeadMinutes;
    }
}
