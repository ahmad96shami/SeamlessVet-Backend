namespace VetSystem.Application.Reports.Contracts;

/// <summary>One product's row in the pharmacy-profit report (M20): the period totals for that product.</summary>
public sealed record PharmacyProfitRow(
    Guid ProductId,
    string ProductName,
    decimal QuantitySold,
    decimal Revenue,
    decimal Cost,
    decimal Profit);

/// <summary>
/// Pharmacy-profit report (M20, PRD §7.9). The clinic's drug/product gross margin over the window's
/// <em>effective</em> (non-void) invoices: <c>Σ product-line revenue − Σ cost_price×qty</c>, restricted
/// to <c>invoice_items</c> that carry a <c>product_id</c> (service lines are excluded). <see cref="Cost"/>
/// is the same Σ <c>cost_price×qty</c> over product lines that the clinic-profits report calls COGS, so
/// the two reconcile to the cent on the same window. <see cref="Revenue"/> is the product lines'
/// <c>line_total</c> (post line-discount, ex-tax); <see cref="Profit"/> = revenue − cost.
/// <see cref="Rows"/> break the margin down per product (paged); the summary spans the whole window.
/// </summary>
public sealed record PharmacyProfitReportResponse(
    DateOnly? From,
    DateOnly? To,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    int TotalCount,
    IReadOnlyList<PharmacyProfitRow> Rows);
