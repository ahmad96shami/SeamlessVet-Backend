using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Appointments;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Appointments;

/// <inheritdoc cref="IAppointmentService"/>
public sealed class AppointmentService : IAppointmentService
{
    private static readonly string[] OccupyingStatuses = AppointmentStatus.OccupiesSlot.ToArray();

    private readonly ApplicationDbContext _db;

    public AppointmentService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> CheckConflictAsync(
        Guid doctorId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        // Bound the candidate scan to what could possibly overlap [from, to):
        //   • lower: an existing slot can only reach into `from` if it started no earlier than
        //     (from − MaxDurationMin), since durations are capped at MaxDurationMin (validated).
        //   • upper: a slot starting at/after `to` can't overlap a half-open window.
        // Both bounds ride the (environment_id, doctor_id, scheduled_at) index. The global query
        // filter already scopes by environment and excludes soft-deleted rows.
        var earliestStart = from.AddMinutes(-AppointmentSchedule.MaxDurationMin);

        var candidates = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId
                && OccupyingStatuses.Contains(a.Status)
                && a.ScheduledAt > earliestStart
                && a.ScheduledAt < to
                && (excludeAppointmentId == null || a.Id != excludeAppointmentId))
            .Select(a => new { a.Id, a.ScheduledAt, a.DurationMin })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var candidateEnd = AppointmentSchedule.EndOf(candidate.ScheduledAt, candidate.DurationMin);
            if (AppointmentSchedule.Overlaps(from, to, candidate.ScheduledAt, candidateEnd))
            {
                return candidate.Id;
            }
        }

        return null;
    }
}
