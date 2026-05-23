using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Field-doctor-visits report (M12 task 10, PRD §7.9). Read-only, environment-scoped. As a feed-like
/// long list it is <b>cursor-paginated</b> (M12 task 16): the field visits are ordered newest-first
/// (<c>CreatedAt DESC, Id DESC</c>) and a keyset cursor advances strictly past the last row, so paging
/// is stable as new visits arrive. It then attaches each page visit's procedures (left-joined to the
/// <c>services</c> catalog for the name) and prescriptions (left-joined to <c>products</c>) so the log
/// shows the services and medications recorded on the visit.
/// </summary>
public sealed class FieldDoctorVisitsReportService
{
    private readonly ApplicationDbContext _db;

    public FieldDoctorVisitsReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<FieldDoctorVisitsReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, Guid? doctorId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var (start, end) = ReportQuery.ResolveWindow(from, to);

        var query = _db.Visits.AsNoTracking()
            .Where(v => v.VisitType == VisitType.Field && v.CreatedAt >= start && v.CreatedAt < end);
        if (doctorId is { } d) query = query.Where(v => v.DoctorId == d);

        var total = await query.CountAsync(cancellationToken);

        var pageSize = CursorPagination.ClampLimit(limit);
        var after = CursorPagination.Decode(cursor);
        if (after is { } c)
        {
            // Keyset: rows strictly "older" than the cursor under CreatedAt DESC, Id DESC.
            query = query.Where(v =>
                v.CreatedAt < c.CreatedAt || (v.CreatedAt == c.CreatedAt && v.Id.CompareTo(c.Id) < 0));
        }

        // Fetch one extra row to tell whether a further page exists without a second query.
        var page = await query
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .Take(pageSize + 1)
            .Select(v => new
            {
                v.Id, v.VisitNumber, v.DoctorId, v.CustomerId, v.PetId, v.Status, v.StartedAt, v.EndedAt, v.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        var visits = hasMore ? page.Take(pageSize).ToList() : page;
        var nextCursor = hasMore && visits.Count > 0
            ? CursorPagination.Encode(visits[^1].CreatedAt, visits[^1].Id)
            : null;

        var visitIds = visits.Select(v => v.Id).ToList();

        var procedures = await (
            from p in _db.Procedures.AsNoTracking()
            where visitIds.Contains(p.VisitId)
            join s in _db.Services.AsNoTracking() on p.ServiceId equals s.Id into sj
            from s in sj.DefaultIfEmpty()
            select new { p.VisitId, p.ServiceId, ServiceName = s != null ? s.NameAr : null, p.Price })
            .ToListAsync(cancellationToken);

        var prescriptions = await (
            from pr in _db.Prescriptions.AsNoTracking()
            where visitIds.Contains(pr.VisitId)
            join pd in _db.Products.AsNoTracking() on pr.ProductId equals pd.Id into pj
            from pd in pj.DefaultIfEmpty()
            select new
            {
                pr.VisitId, pr.ProductId, ProductName = pd != null ? pd.NameAr : null, pr.Dosage, pr.Quantity, pr.DispenseType,
            })
            .ToListAsync(cancellationToken);

        var servicesByVisit = procedures
            .GroupBy(p => p.VisitId)
            .ToDictionary(g => g.Key, g => g
                .Select(p => new FieldVisitServiceLine(p.ServiceId, p.ServiceName, p.Price))
                .ToList());

        var medicationsByVisit = prescriptions
            .GroupBy(m => m.VisitId)
            .ToDictionary(g => g.Key, g => g
                .Select(m => new FieldVisitMedicationLine(m.ProductId, m.ProductName, m.Dosage, m.Quantity, m.DispenseType))
                .ToList());

        var rows = visits
            .Select(v => new FieldVisitRow(
                v.Id, v.VisitNumber, v.DoctorId, v.CustomerId, v.PetId, v.Status, v.StartedAt, v.EndedAt,
                servicesByVisit.TryGetValue(v.Id, out var services) ? services : [],
                medicationsByVisit.TryGetValue(v.Id, out var medications) ? medications : []))
            .ToList();

        return new FieldDoctorVisitsReportResponse(from, to, doctorId, total, rows, nextCursor);
    }
}
