namespace VetSystem.Application.Prescriptions.Contracts;

/// <summary>
/// SCHEMA §6 prescription payload (PRD §5.2-D). <c>DispenseType</c> drives fulfilment:
/// <c>administered_in_clinic</c> deducts <c>Quantity</c> from inventory at create time;
/// <c>dispensed_to_owner</c> raises an event for the POS invoice (M7). <c>Quantity</c> is required
/// (a positive amount) since both paths act on it. <c>Id</c> is client-generated.
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
    decimal? Quantity);

/// <summary>
/// Edits the advisory text only. Product, quantity, and dispense type are immutable after create
/// because <c>administered_in_clinic</c> already moved inventory — a change there would desync the
/// append-only ledger of movements. Correct stock with a compensating movement instead.
/// </summary>
public sealed record PrescriptionPatchRequest(
    string? Dosage,
    string? Frequency,
    string? Duration,
    string? Notes);

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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
