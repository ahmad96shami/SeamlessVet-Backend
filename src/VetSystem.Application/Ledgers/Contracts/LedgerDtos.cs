namespace VetSystem.Application.Ledgers.Contracts;

/// <summary>
/// Append-only ledger entry payload (SCHEMA §2 / "Key invariants" #3). The single write path
/// for every financial event: invoices (M7), receipt vouchers (M7), exam fees (M7),
/// adjustments. <c>IdempotencyKey</c> is mandatory and unique per environment so retries
/// from offline clients collapse to one row.
/// </summary>
public sealed record LedgerEntryRequest(
    Guid? Id,
    Guid LedgerId,
    string EntryType,
    decimal Amount,
    Guid? InvoiceId,
    Guid? ReceiptVoucherId,
    string? Description,
    string IdempotencyKey);

public sealed record LedgerEntryResponse(
    Guid Id,
    Guid LedgerId,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    Guid? InvoiceId,
    Guid? ReceiptVoucherId,
    string? Description,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

public sealed record LedgerResponse(
    Guid Id,
    Guid CustomerId,
    decimal Balance,
    string Status,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Account statement DTO returned by <c>GET /customers/{id}/statement</c> (M3 task 8).
/// Drives the WhatsApp/email/print client paths — contains everything the renderer needs.
/// </summary>
public sealed record StatementResponse(
    Guid CustomerId,
    string CustomerName,
    Guid LedgerId,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<LedgerEntryResponse> Entries);
