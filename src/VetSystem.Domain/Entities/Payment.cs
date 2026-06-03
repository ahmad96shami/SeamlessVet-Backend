using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — a payment against an invoice. An invoice can carry several rows for a mixed payment
/// (PRD §5.4: cash + card + …). A <see cref="PaymentMethod.Credit"/> row is the customer-facing
/// label for the portion left on the ledger; only non-credit methods settle the balance immediately.
/// M19: a <see cref="PaymentMethod.Cheque"/> leg settles immediately (per the PO decision) and may
/// carry optional cheque metadata; a bounced cheque is corrected with an append-only adjustment.
/// </summary>
public sealed class Payment : Entity
{
    public Guid InvoiceId { get; set; }

    public string Method { get; set; } = PaymentMethod.Cash;

    public decimal Amount { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    public string? ChequeNumber { get; set; }

    public string? ChequeBank { get; set; }

    public DateOnly? ChequeDueDate { get; set; }
}

public static class PaymentMethod
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string BankTransfer = "bank_transfer";
    public const string Credit = "credit";

    /// <summary>M19 — a cheque. Settles immediately (no clearing lifecycle); carries optional metadata.</summary>
    public const string Cheque = "cheque";

    public static readonly IReadOnlyCollection<string> All = [Cash, Card, BankTransfer, Credit, Cheque];

    /// <summary>Methods that settle immediately (reduce the ledger debt at issuance). Credit does not.</summary>
    public static readonly IReadOnlyCollection<string> Immediate = [Cash, Card, BankTransfer, Cheque];

    /// <summary>Methods valid for paying a supplier (no <c>credit</c> — credit is customer-AR only).</summary>
    public static readonly IReadOnlyCollection<string> Supplier = [Cash, Card, BankTransfer, Cheque];
}
