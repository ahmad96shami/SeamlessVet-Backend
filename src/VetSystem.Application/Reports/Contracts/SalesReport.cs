namespace VetSystem.Application.Reports.Contracts;

/// <summary>One payment-method row in the sales report (M12 task 7, PRD §7.9).</summary>
public sealed record SalesByMethod(string Method, decimal Amount, int PaymentCount);

/// <summary>
/// Daily/monthly sales report (PRD §7.9): money taken in over a period, broken down by payment method,
/// optionally for one cashier (<c>invoices.issued_by</c>). Only effective (non-void) invoices count;
/// a <c>credit</c> row is the on-account portion. <see cref="Total"/> is the sum across methods;
/// <see cref="InvoiceCount"/> is the number of effective invoices in scope.
/// </summary>
public sealed record SalesReportResponse(
    DateOnly? From,
    DateOnly? To,
    Guid? CashierId,
    decimal Total,
    int InvoiceCount,
    IReadOnlyList<SalesByMethod> ByMethod);
