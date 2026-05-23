namespace VetSystem.Application.Reports.Contracts;

/// <summary>One due vaccination in the upcoming-vaccinations report (M12 task 11, PRD §7.9).</summary>
public sealed record UpcomingVaccinationRow(
    Guid Id,
    Guid? PetId,
    Guid? CustomerId,
    Guid? VisitId,
    string VaccineType,
    DateOnly DateGiven,
    DateOnly? NextDueDate);

/// <summary>
/// Upcoming-vaccinations report (PRD §7.9). Vaccinations whose <c>next_due_date</c> falls in the
/// <c>[from, to]</c> day range (inclusive), optionally for one customer, ordered by due date.
/// <see cref="TotalCount"/> is the whole filtered set; <see cref="Rows"/> is the requested page.
/// </summary>
public sealed record UpcomingVaccinationsReportResponse(
    DateOnly? From,
    DateOnly? To,
    Guid? CustomerId,
    int TotalCount,
    IReadOnlyList<UpcomingVaccinationRow> Rows);
