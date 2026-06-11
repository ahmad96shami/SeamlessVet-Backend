using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M30 (SCHEMA §4) — a payment the clinic makes to a doctor-partner (the AP mirror of
/// <see cref="SupplierPayment"/> / <see cref="ReceiptVoucher"/>). Posting one appends a <c>payment</c>
/// entry that reduces the doctor's <see cref="DoctorPartnerLedger"/> balance. Append-only and
/// idempotent per environment so a retried payment never double-debits. <see cref="Method"/> is one of
/// cash / card / bank_transfer / cheque (never <c>credit</c> — credit is a customer-AR concept). A
/// cheque settles immediately and carries optional <see cref="ChequeNumber"/> / <see cref="ChequeBank"/>
/// / <see cref="ChequeDueDate"/> metadata; a bounced cheque is corrected with an <c>adjustment</c> entry.
/// </summary>
public sealed class DoctorPartnerPayment : Entity
{
    public Guid DoctorPartnerId { get; set; }

    public decimal Amount { get; set; }

    public string Method { get; set; } = PaymentMethod.Cash;

    public Guid PaidBy { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    public string? Notes { get; set; }

    public string? ChequeNumber { get; set; }

    public string? ChequeBank { get; set; }

    public DateOnly? ChequeDueDate { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
