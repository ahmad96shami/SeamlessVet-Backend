namespace VetSystem.Application.Visits.Contracts;

/// <summary>
/// SCHEMA §6 visit payload. <c>Id</c> and <c>VisitNumber</c> are client-generated (Guid v7 +
/// per-user-prefixed number) so dedicated and sync writes converge offline-safely. <c>batch_id</c>
/// / <c>contract_id</c> are intentionally absent here — their FK targets land in M8. A visit may be
/// created <c>open</c> or <c>in_progress</c>; it can never be created already closed.
/// </summary>
public sealed record VisitCreateRequest(
    Guid? Id,
    string VisitType,
    string? VisitNumber,
    Guid CustomerId,
    Guid? PetId,
    Guid DoctorId,
    Guid? ReceptionistId,
    string? Status,
    DateTimeOffset? StartedAt,
    // A. initial assessment
    string? ChiefComplaint,
    string? Symptoms,
    decimal? Temperature,
    int? HeartRate,
    int? RespiratoryRate,
    decimal? Weight,
    string? ClinicalNotes,
    // B. diagnosis
    string? PreliminaryDiagnosis,
    string? FinalDiagnosis,
    string? Severity,
    string? IcdVetCode,
    // field-visit exam fee snapshot
    decimal? ExamFeeApplied);

/// <summary>
/// Section-level update (PRD §5.2 task 5). Every field is optional — only the supplied ones change.
/// <c>Status</c> may advance the visit (e.g. <c>open → in_progress</c>) but cannot close it: use
/// <c>POST /visits/{id}/complete</c> or <c>/cancel</c> for terminal transitions. Editing a visit
/// that is already <c>completed</c>/<c>cancelled</c> is rejected (server-authoritative).
/// </summary>
public sealed record VisitPatchRequest(
    string? Status,
    string? ChiefComplaint,
    string? Symptoms,
    decimal? Temperature,
    int? HeartRate,
    int? RespiratoryRate,
    decimal? Weight,
    string? ClinicalNotes,
    string? PreliminaryDiagnosis,
    string? FinalDiagnosis,
    string? Severity,
    string? IcdVetCode,
    decimal? ExamFeeApplied);

public sealed record VisitResponse(
    Guid Id,
    string VisitType,
    string? VisitNumber,
    Guid CustomerId,
    Guid? PetId,
    Guid? BatchId,
    Guid? ContractId,
    Guid DoctorId,
    Guid? ReceptionistId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? ChiefComplaint,
    string? Symptoms,
    decimal? Temperature,
    int? HeartRate,
    int? RespiratoryRate,
    decimal? Weight,
    string? ClinicalNotes,
    string? PreliminaryDiagnosis,
    string? FinalDiagnosis,
    string? Severity,
    string? IcdVetCode,
    decimal? ExamFeeApplied,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
