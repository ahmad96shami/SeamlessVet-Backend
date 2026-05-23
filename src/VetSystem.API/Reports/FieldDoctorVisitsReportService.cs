using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Field-doctor-visits report (M12 task 10, PRD §7.9). Read-only, environment-scoped. Pages the field
/// visits (newest first) over the window, optionally for one doctor, then attaches each page visit's
/// procedures (left-joined to the <c>services</c> catalog for the name) and prescriptions (left-joined
/// to <c>products</c>) so the log shows the services and medications recorded on the visit.
/// </summary>
public sealed class FieldDoctorVisitsReportService
{
    private readonly ApplicationDbContext _db;

    public FieldDoctorVisitsReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<FieldDoctorVisitsReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, Guid? doctorId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var (start, end) = ReportQuery.ResolveWindow(from, to);

        var query = _db.Visits.AsNoTracking()
            .Where(v => v.VisitType == VisitType.Field && v.CreatedAt >= start && v.CreatedAt < end);
        if (doctorId is { } d) query = query.Where(v => v.DoctorId == d);

        var total = await query.CountAsync(cancellationToken);

        var visits = await query
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .Select(v => new
            {
                v.Id, v.VisitNumber, v.DoctorId, v.CustomerId, v.PetId, v.Status, v.StartedAt, v.EndedAt,
            })
            .ToListAsync(cancellationToken);

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

        return new FieldDoctorVisitsReportResponse(from, to, doctorId, total, rows);
    }
}
