using FluentValidation;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Ledgers.Validators;

public sealed class LedgerEntryRequestValidator : AbstractValidator<LedgerEntryRequest>
{
    public LedgerEntryRequestValidator()
    {
        RuleFor(r => r.LedgerId).NotEmpty();
        RuleFor(r => r.EntryType)
            .NotEmpty()
            .Must(t => LedgerEntryType.All.Contains(t))
            .WithMessage($"EntryType must be one of: {string.Join(", ", LedgerEntryType.All)}.");
        RuleFor(r => r.Amount).NotEqual(0m).WithMessage("Amount must be non-zero (signed).");
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Description).MaximumLength(2048);

        // SCHEMA §2 — polymorphic source: only one of invoice_id / receipt_voucher_id may be set,
        // and they must be consistent with entry_type.
        RuleFor(r => r)
            .Must(r => !(r.InvoiceId.HasValue && r.ReceiptVoucherId.HasValue))
            .WithMessage("invoice_id and receipt_voucher_id are mutually exclusive on a ledger entry.");
    }
}
