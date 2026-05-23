using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — Sanad Qabd: acknowledges a payment received from a customer (PRD §6.2). Posting one
/// appends a <c>receipt_voucher</c> ledger entry that reduces the customer's balance. Append-only
/// and idempotent per environment so a retried offline collection never double-credits the ledger.
/// </summary>
public sealed class ReceiptVoucher : Entity
{
    public Guid CustomerId { get; set; }

    public decimal Amount { get; set; }

    public string Method { get; set; } = PaymentMethod.Cash;

    public Guid IssuedBy { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public string? Notes { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
