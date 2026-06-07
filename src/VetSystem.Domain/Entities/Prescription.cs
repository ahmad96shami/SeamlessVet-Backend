using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — medication prescribed within a <see cref="Visit"/> (PRD §5.2-D).
/// <see cref="DispenseType"/> discriminates the fulfilment path: <c>administered_in_clinic</c>
/// deducts from inventory at create time (M5 task 9); <c>dispensed_to_owner</c> is queued for the
/// POS invoice (M7, M5 task 10).
/// <para>
/// M18 — a dispensed drug the owner administers at home can carry a recurring-dose <b>reminder
/// schedule</b>: when <see cref="ReminderEnabled"/> is set, doses fall at
/// <see cref="StartAt"/> + k·<see cref="IntervalMinutes"/> (k = 0,1,2,…), bounded by whichever of
/// <see cref="DosesCount"/> / <see cref="EndAt"/> is set, and <c>MedicationDueJob</c> fires a
/// <c>medication_due</c> notification <see cref="LeadMinutes"/> ahead of each dose. The job keeps
/// itself exactly-once per dose via the server-managed <see cref="LastRemindedDose"/> high-water mark.
/// </para>
/// </summary>
public sealed class Prescription : Entity
{
    public Guid VisitId { get; set; }

    public Guid ProductId { get; set; }

    public string? Dosage { get; set; }

    public string? Frequency { get; set; }

    public string? Duration { get; set; }

    public string? Notes { get; set; }

    public string DispenseType { get; set; } = Entities.DispenseType.AdministeredInClinic;

    public decimal? Quantity { get; set; }

    /// <summary>
    /// M23 — when true, an <c>administered_in_clinic</c> prescription is charged to the customer:
    /// it assembles into the visit's POS invoice like a dispensed one (stock was already deducted
    /// at recording, so issuance skips the deduction). Meaningless for <c>dispensed_to_owner</c>
    /// rows, which always bill. Defaults to false (current behavior: in-clinic meds absorbed).
    /// </summary>
    public bool Billable { get; set; }

    /// <summary>M18 — when true, <c>MedicationDueJob</c> emits recurring dose reminders for this row.</summary>
    public bool ReminderEnabled { get; set; }

    /// <summary>Minutes between doses (the recurrence step). Required when <see cref="ReminderEnabled"/>.</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>
    /// How many minutes <i>before</i> a dose to fire its reminder. Null falls back to the
    /// per-environment default in <c>system_settings.extra</c> (<c>medicationReminder.defaultLeadMinutes</c>).
    /// </summary>
    public int? LeadMinutes { get; set; }

    /// <summary>Instant of the first dose (dose 0). Required when <see cref="ReminderEnabled"/>.</summary>
    public DateTimeOffset? StartAt { get; set; }

    /// <summary>Optional end of the schedule — no dose falls after this instant.</summary>
    public DateTimeOffset? EndAt { get; set; }

    /// <summary>Optional total number of doses — doses 0..(<see cref="DosesCount"/> − 1).</summary>
    public int? DosesCount { get; set; }

    /// <summary>
    /// Server-managed high-water mark: the largest dose index already reminded. <c>MedicationDueJob</c>
    /// advances it after dispatching, so a re-run (or a missed-then-resumed scan) never double-sends.
    /// Clients never write it.
    /// </summary>
    public int? LastRemindedDose { get; set; }
}

public static class DispenseType
{
    public const string AdministeredInClinic = "administered_in_clinic";
    public const string DispensedToOwner = "dispensed_to_owner";

    public static readonly IReadOnlyCollection<string> All = [AdministeredInClinic, DispensedToOwner];
}
