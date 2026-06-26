using Microsoft.EntityFrameworkCore;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Application.Settings;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M11 task 9 — daily scan of <c>vaccinations.next_due_date</c>. For every vaccination whose due date
/// is the configured lead-time ahead (<c>vaccinationReminder.leadDays</c> in <c>system_settings.extra</c>;
/// 0 = on the day itself), notifies the responsible doctor that a vaccination is due (PRD §6.7, §9).
/// The recipient resolves through the visit's doctor → the customer's assigned doctor → the pet owner's
/// assigned doctor, and falls back to the environment's admins so a due reminder is <b>never silently
/// dropped</b>. Idempotent within a day: a vaccination already notified today is skipped, so a manual
/// re-run won't double-send.
/// </summary>
public sealed class VaccinationRemindersJob
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationRecipientResolver _recipients;
    private readonly IClock _clock;

    public VaccinationRemindersJob(
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
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var todayStart = JobHelpers.StartOfTodayUtc(_clock);

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            // Fire `leadDays` ahead of the due date (0 = on the day). The target date is what the scan
            // matches, so a vaccination due on X is reminded on X − leadDays.
            var leadDays = await LeadDaysAsync(environmentId, cancellationToken);
            var targetDate = today.AddDays(leadDays);

            var due = await _db.Vaccinations
                .IgnoreQueryFilters()
                .Where(v => v.EnvironmentId == environmentId && v.DeletedAt == null && v.NextDueDate == targetDate)
                .Select(v => new { v.Id, v.VisitId, v.CustomerId, v.PetId, v.VaccineType, v.NextDueDate })
                .ToListAsync(cancellationToken);

            if (due.Count == 0)
            {
                continue;
            }

            var notifiedPayloads = await _db.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.EnvironmentId == environmentId
                            && n.Type == NotificationType.VaccinationDue
                            && n.CreatedAt >= todayStart)
                .Select(n => n.Payload)
                .ToListAsync(cancellationToken);

            var alreadyNotified = notifiedPayloads
                .Select(p => JobHelpers.TryReadPayloadGuid(p, "VaccinationId"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            foreach (var vaccination in due)
            {
                if (alreadyNotified.Contains(vaccination.Id))
                {
                    continue;
                }

                Guid? doctorId = null;
                if (vaccination.VisitId is { } visitId)
                {
                    doctorId = await _db.Visits
                        .IgnoreQueryFilters()
                        .Where(x => x.Id == visitId)
                        .Select(x => (Guid?)x.DoctorId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (doctorId is null && vaccination.CustomerId is { } customerId)
                {
                    doctorId = await _db.Customers
                        .IgnoreQueryFilters()
                        .Where(c => c.Id == customerId)
                        .Select(c => c.AssignedDoctorId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                // A pet-only vaccination (no visit, no customer) resolves through its owner — without this
                // the standalone "specific pet" path produced a vaccination with no recipient and was
                // silently dropped.
                if (doctorId is null && vaccination.PetId is { } petId)
                {
                    doctorId = await _db.Pets
                        .IgnoreQueryFilters()
                        .Where(p => p.Id == petId)
                        .Join(
                            _db.Customers.IgnoreQueryFilters(),
                            p => p.CustomerId,
                            c => c.Id,
                            (_, c) => c.AssignedDoctorId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                // Never silently drop a due reminder: if no doctor resolves (e.g. the owner has no
                // assigned doctor), fall back to the environment's admins so someone is notified.
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
                        NotificationType.VaccinationDue,
                        Title: "تطعيم مستحق",
                        Body: $"يوجد تطعيم ({vaccination.VaccineType}) مستحق بتاريخ {vaccination.NextDueDate:yyyy-MM-dd}.",
                        Payload: new
                        {
                            VaccinationId = vaccination.Id,
                            vaccination.VisitId,
                            vaccination.CustomerId,
                            vaccination.PetId,
                            vaccination.VaccineType,
                            vaccination.NextDueDate,
                        }),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// The configured "fire X days before due" lead (<c>vaccinationReminder.leadDays</c> in the
    /// <c>system_settings.extra</c> bag); 0 (the default) means remind on the due date itself.
    /// </summary>
    private async Task<int> LeadDaysAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var extra = await _db.SystemSettings
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => s.Extra)
            .FirstOrDefaultAsync(cancellationToken);

        return VaccinationReminderSettings.FromExtra(extra).LeadDays;
    }
}
