using Microsoft.EntityFrameworkCore;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M11 task 15 — scheduled report delivery to admins and accountants on a cadence (PRD §9). The report
/// <i>content</i> (Excel/PDF generation, email transport) is M12's job; this job owns the cadence and
/// the in-app delivery notification so the wiring exists and is testable now — mirroring how M10 built
/// the profit-distribution service ahead of M12's reports. Idempotent per day.
/// </summary>
public sealed class ScheduledReportDeliveryJob
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationRecipientResolver _recipients;
    private readonly IClock _clock;

    public ScheduledReportDeliveryJob(
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
        var todayStart = JobHelpers.StartOfTodayUtc(_clock);

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            var recipients = await _recipients.ByRolesAsync(
                environmentId, [RoleKey.Admin, RoleKey.Accountant], cancellationToken);
            if (recipients.Count == 0)
            {
                continue;
            }

            var alreadyNotified = (await _db.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.EnvironmentId == environmentId
                            && n.Type == NotificationType.ReportDelivery
                            && n.CreatedAt >= todayStart)
                .Select(n => n.UserId)
                .ToListAsync(cancellationToken)).ToHashSet();

            var targets = recipients.Where(r => !alreadyNotified.Contains(r)).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            await _dispatcher.DispatchAsync(
                new NotificationDispatch(
                    environmentId,
                    targets,
                    NotificationType.ReportDelivery,
                    Title: "التقارير الدورية جاهزة",
                    Body: "التقارير الدورية متاحة للمراجعة في لوحة التقارير.",
                    Payload: new { GeneratedAt = _clock.UtcNow }),
                cancellationToken);
        }
    }
}
