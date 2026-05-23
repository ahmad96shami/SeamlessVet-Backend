namespace VetSystem.Application.Reports.Contracts;

/// <summary>A procedure line on a field visit (the catalog service performed + its snapshot price).</summary>
public sealed record FieldVisitServiceLine(Guid? ServiceId, string? ServiceName, decimal Price);

/// <summary>A prescription line on a field visit (the medication dispensed/administered).</summary>
public sealed record FieldVisitMedicationLine(
    Guid ProductId, string? ProductName, string? Dosage, decimal? Quantity, string DispenseType);

/// <summary>One field visit with its services + medications (M12 task 10, PRD §7.9 "visit log").</summary>
public sealed record FieldVisitRow(
    Guid VisitId,
    string? VisitNumber,
    Guid DoctorId,
    Guid CustomerId,
    Guid? PetId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<FieldVisitServiceLine> Services,
    IReadOnlyList<FieldVisitMedicationLine> Medications);

/// <summary>
/// Field-doctor-visits report (PRD §7.9): the log of field visits (visit_type = field) over a period,
/// optionally for one doctor, each with the services and medications recorded on it. Newest first;
/// <see cref="TotalCount"/> is the whole filtered set, <see cref="Rows"/> the requested page.
/// </summary>
public sealed record FieldDoctorVisitsReportResponse(
    DateOnly? From,
    DateOnly? To,
    Guid? DoctorId,
    int TotalCount,
    IReadOnlyList<FieldVisitRow> Rows);
