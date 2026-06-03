namespace VetSystem.Application.SupplierLedgers.Contracts;

/// <summary>
/// Append-only supplier-ledger entry payload (SCHEMA §4). The single write path for every AP event:
/// purchase invoices (M19), supplier payments (M19), manual adjustments. <c>IdempotencyKey</c> is
/// mandatory and unique per environment so a retried post collapses to one row.
/// </summary>
public sealed record SupplierLedgerEntryRequest(
    Guid? Id,
    Guid SupplierLedgerId,
    string EntryType,
    decimal Amount,
    Guid? PurchaseInvoiceId,
    Guid? SupplierPaymentId,
    string? Description,
    string IdempotencyKey);

public sealed record SupplierLedgerEntryResponse(
    Guid Id,
    Guid SupplierLedgerId,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    Guid? PurchaseInvoiceId,
    Guid? SupplierPaymentId,
    string? Description,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

/// <summary>
/// Supplier account statement returned by <c>GET /suppliers/{id}/statement</c>. Mirrors the customer
/// statement: <see cref="OpeningBalance"/> is the running balance at <c>from</c> (0 if none);
/// <see cref="ClosingBalance"/> is the balance after the last entry in range. Drives the print / share
/// client path.
/// </summary>
public sealed record SupplierStatementResponse(
    Guid SupplierId,
    string SupplierName,
    Guid LedgerId,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<SupplierLedgerEntryResponse> Entries);
