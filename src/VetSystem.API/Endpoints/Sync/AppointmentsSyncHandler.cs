using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Appointments;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/appointments</c> (M6 task 8) — last-write-wins offline write path (PRD §8.4), with the
/// same doctor double-booking detection as the dedicated endpoints (a synced offline booking that
/// collides is rejected). Attendance is deliberately <b>not</b> reachable here: marking an
/// appointment <c>attended</c> opens a clinic visit, so the client must call
/// <c>POST /appointments/{id}/attend</c>. Cancelling / marking no-show is a plain status change and
/// is allowed offline.
/// </summary>
public sealed class AppointmentsSyncHandler : ISyncTableHandler
{
    public const string TableName = "appointments";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IAppointmentService _conflicts;

    public AppointmentsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user, IAppointmentService conflicts)
    {
        _db = db;
        _user = user;
        _conflicts = conflicts;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id, cancellationToken);
        if (existing)
        {
            throw new ConflictException("appointment_already_exists", $"Appointment '{id}' already exists; use PATCH to update.");
        }

        var customerId = SyncBody.OptionalGuid(body, "customer_id");
        var petId = SyncBody.OptionalGuid(body, "pet_id");
        var doctorId = SyncBody.OptionalGuid(body, "doctor_id");
        var serviceId = SyncBody.OptionalGuid(body, "service_id");
        await ValidateReferencesAsync(customerId, petId, doctorId, serviceId, cancellationToken);

        var scheduledAt = SyncBody.OptionalDateTime(body, "scheduled_at")
            ?? throw new ConflictException("invalid_payload", "'scheduled_at' is required and must be an ISO-8601 timestamp.");
        var durationMin = SyncBody.OptionalInt(body, "duration_min");

        var status = SyncBody.OptionalString(body, "status") ?? AppointmentStatus.Scheduled;
        if (!AppointmentStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_appointment_status", $"status '{status}' is not valid.");
        }

        if (doctorId is { } docId && AppointmentStatus.OccupiesSlot.Contains(status))
        {
            await EnsureNoConflictAsync(docId, scheduledAt, durationMin, excludeAppointmentId: null, cancellationToken);
        }

        var appointment = new Appointment
        {
            Id = id,
            CustomerId = customerId,
            PetId = petId,
            DoctorId = doctorId,
            ServiceId = serviceId,
            ScheduledAt = scheduledAt,
            DurationMin = durationMin,
            Status = status,
            Notes = SyncBody.OptionalString(body, "notes"),
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(appointment.Id, appointment.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                          ?? throw new NotFoundException(TableName, id);

        if (SyncBody.TryGetString(body, "status", out var statusValue) && statusValue is not null
            && statusValue != appointment.Status)
        {
            if (statusValue == AppointmentStatus.Attended)
            {
                throw new ConflictException("use_attend_endpoint",
                    "Mark attendance via POST /appointments/{id}/attend so a clinic visit is opened.");
            }

            if (!AppointmentStatus.CanTransition(appointment.Status, statusValue))
            {
                throw new ConflictException("invalid_status_transition",
                    $"Cannot transition an appointment from '{appointment.Status}' to '{statusValue}'.");
            }

            appointment.Status = statusValue;
        }

        var rescheduled = false;
        if (body.TryGetProperty("doctor_id", out _))
        {
            var doctorId = SyncBody.OptionalGuid(body, "doctor_id");
            if (doctorId != appointment.DoctorId)
            {
                if (doctorId is { } did)
                {
                    await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == did, cancellationToken), "doctor", did);
                }
                appointment.DoctorId = doctorId;
                rescheduled = true;
            }
        }

        if (SyncBody.OptionalDateTime(body, "scheduled_at") is { } scheduledAt && scheduledAt != appointment.ScheduledAt)
        {
            appointment.ScheduledAt = scheduledAt;
            rescheduled = true;
        }

        if (body.TryGetProperty("duration_min", out _))
        {
            var durationMin = SyncBody.OptionalInt(body, "duration_min");
            if (durationMin != appointment.DurationMin)
            {
                appointment.DurationMin = durationMin;
                rescheduled = true;
            }
        }

        if (body.TryGetProperty("customer_id", out _))
        {
            var customerId = SyncBody.OptionalGuid(body, "customer_id");
            if (customerId is { } cid)
            {
                await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == cid, cancellationToken), "customer", cid);
            }
            appointment.CustomerId = customerId;
        }

        if (body.TryGetProperty("pet_id", out _)) appointment.PetId = SyncBody.OptionalGuid(body, "pet_id");
        if (body.TryGetProperty("service_id", out _)) appointment.ServiceId = SyncBody.OptionalGuid(body, "service_id");
        if (SyncBody.TryGetString(body, "notes", out var notes)) appointment.Notes = notes;

        if (rescheduled
            && appointment.DoctorId is { } finalDoctorId
            && AppointmentStatus.OccupiesSlot.Contains(appointment.Status))
        {
            await EnsureNoConflictAsync(finalDoctorId, appointment.ScheduledAt, appointment.DurationMin, excludeAppointmentId: id, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(appointment.Id, appointment.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                          ?? throw new NotFoundException(TableName, id);

        _db.Appointments.Remove(appointment);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(appointment.Id, appointment.UpdatedAt);
    }

    private async Task EnsureNoConflictAsync(
        Guid doctorId,
        DateTimeOffset scheduledAt,
        int? durationMin,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        var to = AppointmentSchedule.EndOf(scheduledAt, durationMin);
        var conflictId = await _conflicts.CheckConflictAsync(doctorId, scheduledAt, to, excludeAppointmentId, cancellationToken);
        if (conflictId is { } clash)
        {
            throw new ConflictException("appointment_conflict",
                $"Doctor already has an overlapping appointment ('{clash}') in this slot.");
        }
    }

    private async Task ValidateReferencesAsync(
        Guid? customerId,
        Guid? petId,
        Guid? doctorId,
        Guid? serviceId,
        CancellationToken cancellationToken)
    {
        if (customerId is { } cid)
        {
            await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == cid, cancellationToken), "customer", cid);
        }

        if (doctorId is { } did)
        {
            await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == did, cancellationToken), "doctor", did);
        }

        if (serviceId is { } sid)
        {
            await EnsureExistsAsync(_db.Services.AnyAsync(s => s.Id == sid, cancellationToken), "service", sid);
        }

        if (petId is { } pid)
        {
            var pet = await _db.Pets.FirstOrDefaultAsync(p => p.Id == pid, cancellationToken)
                      ?? throw new NotFoundException("pet", pid);

            if (customerId is { } owner && pet.CustomerId != owner)
            {
                throw new ConflictException("pet_customer_mismatch",
                    "The pet does not belong to the appointment's customer.");
            }
        }
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
