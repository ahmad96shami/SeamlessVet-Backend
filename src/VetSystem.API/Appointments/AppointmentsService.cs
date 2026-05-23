using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Appointments;
using VetSystem.Application.Appointments.Contracts;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Appointments;

/// <summary>
/// Appointment scheduling (PRD §5.3, M6). CRUD plus the lifecycle transitions. Booking and
/// rescheduling run doctor conflict detection through <see cref="IAppointmentService"/>; the status
/// state machine (<see cref="AppointmentStatus.CanTransition"/>) is enforced here, with terminal
/// transitions exposed as dedicated actions (<see cref="AttendAsync"/> / <see cref="CancelAsync"/> /
/// <see cref="NoShowAsync"/>). Attending opens a clinic <see cref="Visit"/> in the same transaction
/// and links it back via <c>appointments.visit_id</c>.
/// </summary>
public sealed class AppointmentsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IAppointmentService _conflicts;

    public AppointmentsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IAppointmentService conflicts)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _conflicts = conflicts;
    }

    public async Task<IReadOnlyList<AppointmentResponse>> ListAsync(
        Guid? doctorId,
        Guid? customerId,
        Guid? petId,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (status is not null && !AppointmentStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_appointment_status", $"status '{status}' is not valid.");
        }

        var query = _db.Appointments.AsNoTracking();

        if (doctorId is { } did) query = query.Where(a => a.DoctorId == did);
        if (customerId is { } cid) query = query.Where(a => a.CustomerId == cid);
        if (petId is { } pid) query = query.Where(a => a.PetId == pid);
        if (status is not null) query = query.Where(a => a.Status == status);
        if (from is { } f) query = query.Where(a => a.ScheduledAt >= f);
        if (to is { } t) query = query.Where(a => a.ScheduledAt <= t);

        var rows = await query
            .OrderBy(a => a.ScheduledAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<AppointmentResponse>).ToList();
    }

    public async Task<AppointmentResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await _db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                          ?? throw new NotFoundException("appointment", id);

        return _mapper.Map<AppointmentResponse>(appointment);
    }

    public async Task<AppointmentResponse> CreateAsync(AppointmentCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        await ValidateReferencesAsync(request.CustomerId, request.PetId, request.DoctorId, request.ServiceId, cancellationToken);

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("appointment_id_collision", $"An appointment with id '{id}' already exists.");
            }
        }

        var status = request.Status ?? AppointmentStatus.Scheduled;

        if (request.DoctorId is { } doctorId && AppointmentStatus.OccupiesSlot.Contains(status))
        {
            await EnsureNoConflictAsync(doctorId, request.ScheduledAt, request.DurationMin, excludeAppointmentId: null, cancellationToken);
        }

        var appointment = new Appointment
        {
            Id = request.Id ?? Guid.Empty,
            CustomerId = request.CustomerId,
            PetId = request.PetId,
            DoctorId = request.DoctorId,
            ServiceId = request.ServiceId,
            ScheduledAt = request.ScheduledAt,
            DurationMin = request.DurationMin,
            Status = status,
            Notes = request.Notes,
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<AppointmentResponse>(appointment);
    }

    public async Task<AppointmentResponse> UpdateAsync(Guid id, AppointmentPatchRequest request, CancellationToken cancellationToken)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                          ?? throw new NotFoundException("appointment", id);

        if (AppointmentStatus.IsTerminal(appointment.Status))
        {
            throw new ConflictException("appointment_locked",
                $"Appointment '{id}' is {appointment.Status}; closed appointments cannot be edited.");
        }

        await ValidateReferencesAsync(request.CustomerId, request.PetId, request.DoctorId, request.ServiceId, cancellationToken);

        var rescheduled = false;
        if (request.DoctorId is { } docId && docId != appointment.DoctorId) { appointment.DoctorId = docId; rescheduled = true; }
        if (request.ScheduledAt is { } at && at != appointment.ScheduledAt) { appointment.ScheduledAt = at; rescheduled = true; }
        if (request.DurationMin is { } dur && dur != appointment.DurationMin) { appointment.DurationMin = dur; rescheduled = true; }

        if (request.CustomerId is { } c) appointment.CustomerId = c;
        if (request.PetId is { } p) appointment.PetId = p;
        if (request.ServiceId is { } s) appointment.ServiceId = s;
        if (request.Notes is not null) appointment.Notes = request.Notes;

        if (request.Status is { } target && target != appointment.Status)
        {
            if (!AppointmentStatus.CanTransition(appointment.Status, target))
            {
                throw new ConflictException("invalid_status_transition",
                    $"Cannot transition an appointment from '{appointment.Status}' to '{target}'.");
            }

            if (AppointmentStatus.IsTerminal(target))
            {
                throw new ConflictException("use_dedicated_endpoint",
                    "Close an appointment via POST /appointments/{id}/attend, /cancel, or /no-show, not PATCH.");
            }

            appointment.Status = target;
        }

        // Re-check the slot whenever the doctor/time/duration moved and the appointment still occupies one.
        if (rescheduled
            && appointment.DoctorId is { } finalDoctorId
            && AppointmentStatus.OccupiesSlot.Contains(appointment.Status))
        {
            await EnsureNoConflictAsync(finalDoctorId, appointment.ScheduledAt, appointment.DurationMin, excludeAppointmentId: id, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<AppointmentResponse>(appointment);
    }

    /// <summary>
    /// M6 task 6 — opens a clinic visit (in_clinic, open) for the appointment and transitions it to
    /// <c>attended</c>, linking the two via <c>appointments.visit_id</c>. Idempotent: an already
    /// attended appointment returns its existing visit without opening a second one.
    /// </summary>
    public async Task<AppointmentResponse> AttendAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                          ?? throw new NotFoundException("appointment", id);

        if (appointment.Status == AppointmentStatus.Attended)
        {
            return _mapper.Map<AppointmentResponse>(appointment);
        }

        if (!AppointmentStatus.CanTransition(appointment.Status, AppointmentStatus.Attended))
        {
            throw new ConflictException("invalid_status_transition",
                $"Cannot mark an appointment '{appointment.Status}' as attended.");
        }

        if (appointment.CustomerId is not { } customerId || appointment.DoctorId is not { } doctorId)
        {
            throw new ConflictException("appointment_incomplete",
                "An appointment needs a customer and a doctor before it can be attended (a visit is opened for them).");
        }

        var visit = new Visit
        {
            Id = Guid.CreateVersion7(),
            VisitType = VisitType.InClinic,
            CustomerId = customerId,
            PetId = appointment.PetId,
            DoctorId = doctorId,
            Status = VisitStatus.Open,
            StartedAt = _clock.UtcNow,
        };
        _db.Visits.Add(visit);

        appointment.VisitId = visit.Id;
        appointment.Status = AppointmentStatus.Attended;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<AppointmentResponse>(appointment);
    }

    public Task<AppointmentResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
        => CloseAsync(id, AppointmentStatus.Cancelled, cancellationToken);

    public Task<AppointmentResponse> NoShowAsync(Guid id, CancellationToken cancellationToken)
        => CloseAsync(id, AppointmentStatus.NoShow, cancellationToken);

    private async Task<AppointmentResponse> CloseAsync(Guid id, string target, CancellationToken cancellationToken)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                          ?? throw new NotFoundException("appointment", id);

        // Idempotent replay: re-closing to the same terminal state is a no-op.
        if (appointment.Status == target)
        {
            return _mapper.Map<AppointmentResponse>(appointment);
        }

        if (!AppointmentStatus.CanTransition(appointment.Status, target))
        {
            throw new ConflictException("invalid_status_transition",
                $"Cannot transition an appointment from '{appointment.Status}' to '{target}'.");
        }

        appointment.Status = target;
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<AppointmentResponse>(appointment);
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
            await RequireExistsAsync(_db.Customers.AnyAsync(c => c.Id == cid, cancellationToken), "customer", cid);
        }

        if (doctorId is { } did)
        {
            await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == did, cancellationToken), "doctor", did);
        }

        if (serviceId is { } sid)
        {
            await RequireExistsAsync(_db.Services.AnyAsync(s => s.Id == sid, cancellationToken), "service", sid);
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

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
