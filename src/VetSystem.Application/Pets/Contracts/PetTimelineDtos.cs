namespace VetSystem.Application.Pets.Contracts;

/// <summary>
/// Medical timeline for a pet (PRD §5.2 "Medical Timeline", M5 task 17): every visit — in-clinic
/// and field — merged chronologically (most recent first), each carrying its procedures,
/// prescriptions, and vaccinations. A read model only; no entity is mutated.
/// </summary>
public sealed record PetTimelineResponse(
    Guid PetId,
    IReadOnlyList<PetTimelineVisit> Visits);

public sealed record PetTimelineVisit(
    Guid VisitId,
    string VisitType,
    string? VisitNumber,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    Guid DoctorId,
    string? PreliminaryDiagnosis,
    string? FinalDiagnosis,
    IReadOnlyList<TimelineProcedure> Procedures,
    IReadOnlyList<TimelinePrescription> Prescriptions,
    IReadOnlyList<TimelineVaccination> Vaccinations);

public sealed record TimelineProcedure(Guid Id, Guid? ServiceId, string? ResultText, decimal Price);

public sealed record TimelinePrescription(
    Guid Id, Guid ProductId, string DispenseType, decimal? Quantity, string? Dosage);

public sealed record TimelineVaccination(Guid Id, string VaccineType, DateOnly DateGiven, DateOnly? NextDueDate);
