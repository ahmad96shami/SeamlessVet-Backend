using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Doctor-income report (M12 task 2, PRD §7.9). Read-only over <see cref="ApplicationDbContext"/>;
/// the global environment query filter scopes every query to the caller's environment, so reports
/// never leak across environments (SCHEMA invariant #6).
///
/// <para><b>Attribution (v1):</b> a doctor's <b>visits</b> are <c>visits.doctor_id = D</c> opened in the
/// window (optionally one <c>visit_type</c>); their <b>revenue</b> is the net-of-void total of invoices
/// linked to those visits (<c>invoices.visit_id → visit</c>) issued in the window — a walk-in POS sale
/// carries no visit, so it is clinic revenue, not a doctor's; their <b>share</b> is
/// Σ <c>doctor_entitlements.computed_amount</c> accrued to the doctor in the window. Void handling
/// mirrors <c>EntitlementService</c>: a <c>void</c> reversal row and its voided original both drop out.</para>
/// </summary>
public sealed class DoctorIncomeReportService
{
    private readonly ApplicationDbContext _db;

    public DoctorIncomeReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DoctorIncomeReportResponse> BuildAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? doctorId,
        string? visitType,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (visitType is not null && !VisitType.All.Contains(visitType))
        {
            throw new ConflictException("invalid_visit_type", $"visit_type '{visitType}' is not valid.");
        }

        var (start, end) = ReportQuery.ResolveWindow(from, to);

        // Visit count per doctor — opened in the window (by created_at), optional type filter.
        var visitQuery = _db.Visits.AsNoTracking().Where(v => v.CreatedAt >= start && v.CreatedAt < end);
        if (doctorId is { } vd) visitQuery = visitQuery.Where(v => v.DoctorId == vd);
        if (visitType is not null) visitQuery = visitQuery.Where(v => v.VisitType == visitType);

        var visitCounts = (await visitQuery
            .GroupBy(v => v.DoctorId)
            .Select(g => new { DoctorId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DoctorId, x => x.Count);

        // Revenue — invoices linked to the doctor's visits, issued in the window, net of void. The
        // void rows must be in scope to drop their originals, so net-of-void is applied in memory.
        var invoiceJoin =
            from i in _db.Invoices.AsNoTracking()
            join v in _db.Visits.AsNoTracking() on i.VisitId equals v.Id
            where i.IssuedAt >= start && i.IssuedAt < end
            select new { i.Id, i.Total, i.Status, i.VoidOfInvoiceId, v.DoctorId, v.VisitType };
        if (doctorId is { } id2) invoiceJoin = invoiceJoin.Where(x => x.DoctorId == id2);
        if (visitType is not null) invoiceJoin = invoiceJoin.Where(x => x.VisitType == visitType);

        var invoiceRows = await invoiceJoin.ToListAsync(cancellationToken);
        var voidedOriginalIds = invoiceRows
            .Where(x => x.VoidOfInvoiceId is not null)
            .Select(x => x.VoidOfInvoiceId!.Value)
            .ToHashSet();
        var revenueByDoctor = invoiceRows
            .Where(x => x.Status == InvoiceStatus.Issued && x.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(x.Id))
            .GroupBy(x => x.DoctorId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));

        // Share — entitlements accrued to the doctor in the window (by created_at).
        var shareQuery = _db.DoctorEntitlements.AsNoTracking().Where(e => e.CreatedAt >= start && e.CreatedAt < end);
        if (doctorId is { } id3) shareQuery = shareQuery.Where(e => e.DoctorId == id3);

        var shareByDoctor = (await shareQuery
            .GroupBy(e => e.DoctorId)
            .Select(g => new { DoctorId = g.Key, Sum = g.Sum(e => e.ComputedAmount) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DoctorId, x => x.Sum);

        var doctorIds = visitCounts.Keys
            .Concat(revenueByDoctor.Keys)
            .Concat(shareByDoctor.Keys)
            .Distinct()
            .ToList();

        var names = await _db.Users.AsNoTracking()
            .Where(u => doctorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var allRows = doctorIds
            .Select(did => new DoctorIncomeRow(
                did,
                names.TryGetValue(did, out var name) ? name : string.Empty,
                visitCounts.TryGetValue(did, out var count) ? count : 0,
                revenueByDoctor.TryGetValue(did, out var revenue) ? revenue : 0m,
                shareByDoctor.TryGetValue(did, out var share) ? share : 0m))
            .OrderByDescending(r => r.CalculatedShare)
            .ThenByDescending(r => r.TotalRevenue)
            .ThenBy(r => r.DoctorName)
            .ThenBy(r => r.DoctorId)
            .ToList();

        var page = allRows
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .ToList();

        return new DoctorIncomeReportResponse(
            from,
            to,
            visitType,
            DoctorCount: allRows.Count,
            TotalVisitCount: allRows.Sum(r => r.VisitCount),
            TotalRevenue: allRows.Sum(r => r.TotalRevenue),
            TotalCalculatedShare: allRows.Sum(r => r.CalculatedShare),
            Rows: page);
    }
}
