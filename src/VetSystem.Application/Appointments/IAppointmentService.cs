namespace VetSystem.Application.Appointments;

/// <summary>
/// Doctor double-booking detection (M6 task 3). An appointment occupies a half-open
/// <c>[scheduled_at, +duration)</c> window; two windows for the same doctor conflict on any positive
/// overlap (back-to-back slots do not). Only slot-occupying statuses are considered — a
/// <c>cancelled</c>/<c>no_show</c> appointment frees its slot. The pure interval math lives in
/// <c>VetSystem.Domain.Entities.AppointmentSchedule</c> (unit-tested); this DB-aware service applies
/// it across the doctor's existing rows. Re-used by both the dedicated endpoints and
/// <c>/sync/appointments</c>, so the rule lives in exactly one place.
/// </summary>
public interface IAppointmentService
{
    /// <summary>
    /// Returns the id of an existing appointment whose slot overlaps <c>[from, to)</c> for
    /// <paramref name="doctorId"/>, or <c>null</c> if the slot is free. <paramref name="excludeAppointmentId"/>
    /// lets a reschedule ignore its own row.
    /// </summary>
    Task<Guid?> CheckConflictAsync(
        Guid doctorId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken);
}
