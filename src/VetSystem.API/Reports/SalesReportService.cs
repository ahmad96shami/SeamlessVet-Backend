using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Reports;

/// <summary>
/// Sales report (M12 task 7, PRD §7.9). Read-only, environment-scoped. Sums <c>payments</c> against the
/// window's effective (non-void) invoices, grouped by method, optionally narrowed to one cashier
/// (<c>invoices.issued_by</c>). Money-taken-in view: a <c>credit</c> row reflects the on-account
/// portion. The period filters on <c>invoices.issued_at</c> (the sale date).
/// </summary>
public sealed class SalesReportService
{
    private readonly ApplicationDbContext _db;

    public SalesReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SalesReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, Guid? cashierId, CancellationToken cancellationToken)
    {
        var (start, end) = ReportQuery.ResolveWindow(from, to);

        var invoiceQuery = _db.Invoices.AsNoTracking().Where(i => i.IssuedAt >= start && i.IssuedAt < end);
        if (cashierId is { } c) invoiceQuery = invoiceQuery.Where(i => i.IssuedBy == c);

        var invoices = await invoiceQuery
            .Select(i => new { i.Id, i.Status, i.VoidOfInvoiceId })
            .ToListAsync(cancellationToken);

        var voidedOriginalIds = invoices
            .Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();
        var effectiveIds = invoices
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();

        if (effectiveIds.Count == 0)
        {
            return new SalesReportResponse(from, to, cashierId, 0m, 0, []);
        }

        var grouped = await _db.Payments.AsNoTracking()
            .Where(p => effectiveIds.Contains(p.InvoiceId))
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byMethod = grouped
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.Method)
            .Select(x => new SalesByMethod(x.Method, x.Amount, x.Count))
            .ToList();

        return new SalesReportResponse(from, to, cashierId, byMethod.Sum(x => x.Amount), effectiveIds.Count, byMethod);
    }
}
