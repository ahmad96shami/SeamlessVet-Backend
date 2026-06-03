using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M19 (SCHEMA §4) — append-only history of a supplier account, mirroring the customer
/// <see cref="LedgerEntry"/>. UPDATE/DELETE is never accepted; corrections (e.g. a bounced cheque) are
/// new <c>adjustment</c> rows. <see cref="Amount"/> is signed: positive increases the payable (a
/// purchase invoice), negative reduces it (a payment). <see cref="BalanceAfter"/> stores the running
/// balance immediately after this entry applied. The polymorphic
/// <see cref="PurchaseInvoiceId"/> / <see cref="SupplierPaymentId"/> source FKs are nullable (an
/// adjustment has neither).
/// </summary>
public sealed class SupplierLedgerEntry : Entity
{
    public Guid SupplierLedgerId { get; set; }

    public string EntryType { get; set; } = SupplierLedgerEntryType.Adjustment;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public Guid? PurchaseInvoiceId { get; set; }

    public Guid? SupplierPaymentId { get; set; }

    public string? Description { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class SupplierLedgerEntryType
{
    /// <summary>Goods received on a purchase invoice — increases the payable.</summary>
    public const string PurchaseInvoice = "purchase_invoice";

    /// <summary>A payment made to the supplier — reduces the payable.</summary>
    public const string Payment = "payment";

    /// <summary>Manual correction (e.g. a bounced cheque) — signed either way.</summary>
    public const string Adjustment = "adjustment";

    public static readonly IReadOnlyCollection<string> All = [PurchaseInvoice, Payment, Adjustment];
}
