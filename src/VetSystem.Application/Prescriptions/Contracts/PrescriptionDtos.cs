namespace VetSystem.Application.Prescriptions.Contracts;

/// <summary>
/// SCHEMA §6 prescription payload (PRD §5.2-D). <c>DispenseType</c> drives fulfilment:
/// <c>administered_in_clinic</c> deducts <c>Quantity</c> from inventory at create time;
/// <c>dispensed_to_owner</c> raises an event for the POS invoice (M7). <c>Quantity</c> is required
/// (a positive amount) since both paths act on it. <c>Id</c> is client-generated.
/// <para>
/// M18 — set <c>ReminderEnabled</c> with an <c>IntervalMinutes</c> recurrence and a <c>StartAt</c> to
/// have <c>MedicationDueJob</c> fire recurring dose reminders (<c>LeadMinutes</c> ahead of each dose;
/// null falls back to the per-environment default). Bound the schedule with <c>DosesCount</c> and/or
/// <c>EndAt</c>, or leave both null for an open-ended course. Typical for <c>dispensed_to_owner</c>.
/// </para>
/// </summary>
public sealed record PrescriptionCreateRequest(
    Guid? Id,
    Guid VisitId,
    Guid ProductId,
    string? Dosage,
    string? Frequency,
    string? Duration,
    string? Notes,
    string DispenseType,
    decimal? Quantity,
    // M23 — charge an administered_in_clinic med to the customer (assembles into the visit's
    // invoice; stock already moved at recording). Ignored for dispensed_to_owner (always bills).
    bool Billable = false,
    bool ReminderEnabled = false,
    int? IntervalMinutes = null,
    int? LeadMinutes = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    int? DosesCount = null);

/// <summary>
/// Edits the advisory text and the M18 reminder schedule. Product, quantity, and dispense type are
/// immutable after create because <c>administered_in_clinic</c> already moved inventory — a change
/// there would desync the append-only ledger of movements. Correct stock with a compensating movement
/// instead. The reminder schedule may be toggled/retuned freely; changing the dose anchors mid-course
/// never double-sends (the job's dose high-water mark only ever advances). M23 — <c>Billable</c>
/// may be toggled until the prescription is billed on an invoice.
/// </summary>
public sealed record PrescriptionPatchRequest(
    string? Dosage,
    string? Frequency,
    string? Duration,
    string? Notes,
    bool? Billable = null,
    bool? ReminderEnabled = null,
    int? IntervalMinutes = null,
    int? LeadMinutes = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    int? DosesCount = null);

public sealed record PrescriptionResponse(
    Guid Id,
    Guid VisitId,
    Guid ProductId,
    string? Dosage,
    string? Frequency,
    string? Duration,
    string? Notes,
    string DispenseType,
    decimal? Quantity,
    bool Billable,
    bool ReminderEnabled,
    int? IntervalMinutes,
    int? LeadMinutes,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    int? DosesCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
