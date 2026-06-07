using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Reports;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;
using Codes = VetSystem.Domain.Entities;

namespace VetSystem.API.Reports;

/// <summary>
/// Visit-profit report (M20 task 2, PRD §7.9). Read-only, environment-scoped. Computes the gross margin
/// of the invoices attributed to each visit of <see cref="VisitTypeCode"/> over the window — revenue =
/// the invoice's ex-tax total, COGS = <c>cost_price×qty</c> over its product lines — and rolls multiple
/// invoices (e.g. a field invoice plus a standalone exam-fee invoice) up to their visit. The effective
/// (non-void) invoice rule matches <see cref="ClinicProfitsReportService"/>, so summing the in-clinic and
/// field reports over a window reconciles to the clinic-profits net profit once walk-ins (no visit) are
/// excluded. The farm/clinic slicer filters on <c>Visit.FarmId</c> (M16). Concrete reports are the two
/// sealed subclasses below; this base holds the shared query.
/// </summary>
public abstract class VisitProfitReportService
{
    private const string ScopeFarm = "farm";
    private const string ScopeClinic = "clinic";

    private readonly ApplicationDbContext _db;

    protected VisitProfitReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>The visit type this report covers (<c>in_clinic</c> | <c>field</c>).</summary>
    protected abstract string VisitTypeCode { get; }

    public async Task<VisitProfitReportResponse> BuildAsync(
        DateOnly? from, DateOnly? to, string? scope, int? skip, int? take, CancellationToken cancellationToken)
    {
        var visitType = VisitTypeCode;
        var normalizedScope = NormalizeScope(scope);
        var (start, end) = ReportQuery.ResolveWindow(from, to);

        // Effective (non-void) invoices in the window that attribute to a visit (walk-ins carry none).
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.IssuedAt >= start && i.IssuedAt < end && i.VisitId != null)
            .Select(i => new { i.Id, VisitId = i.VisitId!.Value, i.BatchId, i.Total, i.TaxAmount, i.Status, i.VoidOfInvoiceId })
            .ToListAsync(cancellationToken);

        var voidedOriginalIds = invoices
            .Where(i => i.VoidOfInvoiceId is not null)
            .Select(i => i.VoidOfInvoiceId!.Value)
            .ToHashSet();
        var effective = invoices
            .Where(i => i.Status == InvoiceStatus.Issued && i.VoidOfInvoiceId is null && !voidedOriginalIds.Contains(i.Id))
            .ToList();

        if (effective.Count == 0)
        {
            return Empty(from, to, normalizedScope);
        }

        // The visits of this type behind those invoices, sliced farm/clinic by FarmId.
        var visitIds = effective.Select(i => i.VisitId).Distinct().ToList();
        var visitsQuery = _db.Visits.AsNoTracking()
            .Where(v => visitIds.Contains(v.Id) && v.VisitType == visitType);
        visitsQuery = normalizedScope switch
        {
            ScopeFarm => visitsQuery.Where(v => v.FarmId != null),
            ScopeClinic => visitsQuery.Where(v => v.FarmId == null),
            _ => visitsQuery,
        };
        var visits = await visitsQuery
            .Select(v => new { v.Id, v.VisitNumber, v.CustomerId, v.FarmId })
            .ToListAsync(cancellationToken);

        if (visits.Count == 0)
        {
            return Empty(from, to, normalizedScope);
        }

        var keptVisitIds = visits.Select(v => v.Id).ToHashSet();
        var keptInvoices = effective.Where(i => keptVisitIds.Contains(i.VisitId)).ToList();
        var keptInvoiceIds = keptInvoices.Select(i => i.Id).ToList();

        // COGS per invoice (product lines only).
        var costByInvoice = (await _db.InvoiceItems.AsNoTracking()
            .Where(it => keptInvoiceIds.Contains(it.InvoiceId) && it.ProductId != null)
            .GroupBy(it => it.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Cost = g.Sum(x => x.CostPrice * x.Quantity) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.InvoiceId, x => x.Cost);

        // M24 — settled batches re-price their invoices retroactively; the batch-level discount is
        // deliberately absent here (no per-visit basis — it lives in clinic-profits at settled_at).
        var invoiceDeltas = await SettledPriceOverlay.LoadInvoiceDeltasAsync(
            _db,
            keptInvoices.Where(i => i.BatchId is not null).Select(i => (i.Id, i.BatchId!.Value)).ToList(),
            cancellationToken);

        // Roll the invoices up to their visit (a visit may have several).
        var perVisit = keptInvoices
            .GroupBy(i => i.VisitId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Revenue: g.Sum(i => i.Total - i.TaxAmount + invoiceDeltas.GetValueOrDefault(i.Id)),
                    Cogs: g.Sum(i => costByInvoice.TryGetValue(i.Id, out var c) ? c : 0m)));

        var rowsAll = visits
            .Where(v => perVisit.ContainsKey(v.Id))
            .Select(v =>
            {
                var (revenue, cogs) = perVisit[v.Id];
                return new VisitProfitRow(v.Id, v.VisitNumber, v.CustomerId, v.FarmId, revenue, cogs, revenue - cogs);
            })
            .OrderByDescending(r => r.Profit)
            .ThenBy(r => r.VisitId)
            .ToList();

        var totalRevenue = rowsAll.Sum(r => r.Revenue);
        var totalCogs = rowsAll.Sum(r => r.Cogs);

        var page = rowsAll
            .Skip(ReportQuery.ClampSkip(skip))
            .Take(ReportQuery.ClampTake(take))
            .ToList();

        return new VisitProfitReportResponse(
            from, to, visitType, normalizedScope,
            totalRevenue, totalCogs, totalRevenue - totalCogs, rowsAll.Count, page);
    }

    private VisitProfitReportResponse Empty(DateOnly? from, DateOnly? to, string? scope) =>
        new(from, to, VisitTypeCode, scope, 0m, 0m, 0m, 0, []);

    private static string? NormalizeScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        ScopeFarm => ScopeFarm,
        ScopeClinic => ScopeClinic,
        null or "" or "all" => null,
        var other => throw new ConflictException("invalid_scope", $"scope '{other}' is not supported (use farm|clinic)."),
    };
}

/// <summary>In-clinic visit-profit report (M20 task 2): <c>visit_type = 'in_clinic'</c>.</summary>
public sealed class InClinicVisitProfitReportService : VisitProfitReportService
{
    public InClinicVisitProfitReportService(ApplicationDbContext db) : base(db)
    {
    }

    protected override string VisitTypeCode => Codes.VisitType.InClinic;
}

/// <summary>External/field visit-profit report (M20 task 2): <c>visit_type = 'field'</c>.</summary>
public sealed class FieldVisitProfitReportService : VisitProfitReportService
{
    public FieldVisitProfitReportService(ApplicationDbContext db) : base(db)
    {
    }

    protected override string VisitTypeCode => Codes.VisitType.Field;
}
