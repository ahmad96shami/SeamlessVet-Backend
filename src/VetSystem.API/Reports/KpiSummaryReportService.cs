using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Admin-dashboard KPI summary (M12 task 14, PRD §7.9). Read-only, environment-scoped. "Today" and
/// "this month" are computed from <see cref="IClock"/> in UTC and echoed back via
/// <see cref="KpiSummaryResponse.AsOf"/>; revenue is ex-tax and net of void (same basis as the P&amp;L);
/// the low-stock count reuses M4's <see cref="IInventoryScanService"/> so it matches the alert job.
/// </summary>
public sealed class KpiSummaryReportService
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryScanService _inventoryScan;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;

    public KpiSummaryReportService(
        ApplicationDbContext db,
        IInventoryScanService inventoryScan,
        ICurrentUserAccessor currentUser,
        IClock clock)
    {
        _db = db;
        _inventoryScan = inventoryScan;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<KpiSummaryResponse> BuildAsync(CancellationToken cancellationToken)
    {
        var envId = _currentUser.EnvironmentId
            ?? throw new ForbiddenException("unauthenticated", "Authentication required.");

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var dayStart = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var monthStart = new DateTimeOffset(new DateTime(today.Year, today.Month, 1), TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);

        var visitsToday = await _db.Visits.AsNoTracking()
            .CountAsync(v => v.CreatedAt >= dayStart && v.CreatedAt < dayEnd, cancellationToken);

        var monthInvoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.IssuedAt >= monthStart && i.IssuedAt < monthEnd)
            .Select(i => new { i.Id, i.Total, i.TaxAmount, i.Status, i.VoidOfInvoiceId })
            .ToListAsync(cancellationToken);
        var voidedOriginalIds = monthInvoices
            .Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();
        var revenueThisMonth = monthInvoices
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .Sum(i => i.Total - i.TaxAmount);

        var pendingEntitlements = await _db.DoctorEntitlements.AsNoTracking()
            .CountAsync(e => e.Status == EntitlementStatus.Pending, cancellationToken);

        var lowStock = await _inventoryScan.ScanLowStockAsync(envId, cancellationToken);

        return new KpiSummaryResponse(today, visitsToday, revenueThisMonth, pendingEntitlements, lowStock.Count);
    }
}
