using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Upcoming-vaccinations report (M12 task 11, PRD §7.9). Read-only, environment-scoped. Lists
/// vaccinations with a <c>next_due_date</c> in the requested day range (inclusive on both ends, since
/// it is a due-date schedule rather than an instant window), optionally narrowed to one customer,
/// ordered by the soonest due date. Offset-paged; <see cref="UpcomingVaccinationsReportResponse.TotalCount"/>
/// reflects the whole filtered set.
/// </summary>
public sealed class UpcomingVaccinationsReportService
{
    private readonly ApplicationDbContext _db;

    public UpcomingVaccinationsReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<UpcomingVaccinationsReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, Guid? customerId, int? skip, int? take, CancellationToken cancellationToken)
    {
        ReportQuery.EnsureValidPeriod(from, to);

        var query = _db.Vaccinations.AsNoTracking().Where(v => v.NextDueDate != null);
        if (from is { } f) query = query.Where(v => v.NextDueDate >= f);
        if (to is { } t) query = query.Where(v => v.NextDueDate <= t);
        if (customerId is { } c) query = query.Where(v => v.CustomerId == c);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(v => v.NextDueDate)
            .ThenBy(v => v.Id)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .Select(v => new UpcomingVaccinationRow(
                v.Id, v.PetId, v.CustomerId, v.VisitId, v.VaccineType, v.DateGiven, v.NextDueDate))
            .ToListAsync(cancellationToken);

        return new UpcomingVaccinationsReportResponse(from, to, customerId, total, rows);
    }
}
