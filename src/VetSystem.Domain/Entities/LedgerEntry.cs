using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §2 — append-only financial history. UPDATE/DELETE is rejected on every path
/// (SCHEMA "Key invariants" #3); corrections are new <c>adjustment</c> rows.
/// <see cref="Amount"/> is signed: positive increases debt, negative reduces it.
/// <see cref="BalanceAfter"/> stores the running balance immediately after this entry applied.
/// The polymorphic <see cref="InvoiceId"/> / <see cref="ReceiptVoucherId"/> FKs are nullable
/// here; M7 wires them once <c>invoices</c> + <c>receipt_vouchers</c> exist.
/// </summary>
public sealed class LedgerEntry : Entity
{
    public Guid LedgerId { get; set; }

    public string EntryType { get; set; } = LedgerEntryType.Adjustment;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public Guid? InvoiceId { get; set; }

    public Guid? ReceiptVoucherId { get; set; }

    public string? Description { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class LedgerEntryType
{
    public const string Invoice = "invoice";
    public const string ServiceFee = "service_fee";
    public const string ExamFee = "exam_fee";
    public const string ReceiptVoucher = "receipt_voucher";
    public const string Adjustment = "adjustment";

    public static readonly IReadOnlyCollection<string> All =
    [
        Invoice, ServiceFee, ExamFee, ReceiptVoucher, Adjustment,
    ];
}
