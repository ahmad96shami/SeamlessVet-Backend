namespace VetSystem.Application.Reports.Contracts;

/// <summary>One visit's row in a visit-profit report (M20): the visit's revenue, COGS and margin.</summary>
public sealed record VisitProfitRow(
    Guid VisitId,
    string? VisitNumber,
    Guid CustomerId,
    Guid? FarmId,
    decimal Revenue,
    decimal Cogs,
    decimal Profit);

/// <summary>
/// Visit-profit report (M20 task 2, PRD §7.9) — the gross margin of the invoices attributed to a visit,
/// grouped by visit. <see cref="VisitType"/> selects the slice: <c>in_clinic</c> (in-clinic visit
/// profit) or <c>field</c> (external/field visit profit). <see cref="Scope"/> optionally narrows by
/// <c>Visit.FarmId</c> (M16): <c>farm</c> = farm-attributed visits, <c>clinic</c> = pet/clinic visits,
/// <c>null</c> = both. Per visit, <see cref="Revenue"/> is the ex-tax total of its effective (non-void)
/// invoices and <see cref="Cogs"/> the <c>cost_price×qty</c> over their product lines, so summing the
/// in-clinic and field reports over the same window reconciles to the clinic-profits net profit (once
/// walk-in invoices, which carry no visit, are set aside). The summary spans the whole filtered set;
/// <see cref="Rows"/> are offset-paged.
/// </summary>
public sealed record VisitProfitReportResponse(
    DateOnly? From,
    DateOnly? To,
    string VisitType,
    string? Scope,
    decimal Revenue,
    decimal Cogs,
    decimal Profit,
    int VisitCount,
    IReadOnlyList<VisitProfitRow> Rows);
