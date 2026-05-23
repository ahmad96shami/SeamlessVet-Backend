using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M11 task 9 — daily scan of <c>vaccinations.next_due_date</c>. For every vaccination due <i>today</i>
/// (per the injected clock), notifies the responsible doctor (the visit's doctor, else the customer's
/// assigned doctor) that a vaccination is due (PRD §6.7, §9). Idempotent within a day: a vaccination
/// already notified today is skipped, so a manual re-run won't double-send.
/// </summary>
public sealed class VaccinationRemindersJob
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IClock _clock;

    public VaccinationRemindersJob(ApplicationDbContext db, INotificationDispatcher dispatcher, IClock clock)
    {
        _db = db;
        _dispatcher = dispatcher;
        _clock = clock;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var todayStart = JobHelpers.StartOfTodayUtc(_clock);

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            var due = await _db.Vaccinations
                .IgnoreQueryFilters()
                .Where(v => v.EnvironmentId == environmentId && v.DeletedAt == null && v.NextDueDate == today)
                .Select(v => new { v.Id, v.VisitId, v.CustomerId, v.VaccineType, v.NextDueDate })
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
                .Select(p => TryReadGuid(p, "VaccinationId"))
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

                if (doctorId is not { } recipient)
                {
                    continue;
                }

                await _dispatcher.DispatchAsync(
                    new NotificationDispatch(
                        environmentId,
                        [recipient],
                        NotificationType.VaccinationDue,
                        Title: "تطعيم مستحق اليوم",
                        Body: $"يوجد تطعيم ({vaccination.VaccineType}) مستحق بتاريخ {vaccination.NextDueDate:yyyy-MM-dd}.",
                        Payload: new
                        {
                            VaccinationId = vaccination.Id,
                            vaccination.VisitId,
                            vaccination.CustomerId,
                            vaccination.VaccineType,
                            vaccination.NextDueDate,
                        }),
                    cancellationToken);
            }
        }
    }

    private static Guid? TryReadGuid(string? json, string property)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var element) && element.TryGetGuid(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // A malformed payload should never break the scan.
        }

        return null;
    }
}
