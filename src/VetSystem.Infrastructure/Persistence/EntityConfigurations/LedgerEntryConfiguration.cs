using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.LedgerId).HasColumnName("ledger_id").IsRequired();
        builder.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired().HasMaxLength(20);
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)");
        builder.Property(e => e.InvoiceId).HasColumnName("invoice_id");
        builder.Property(e => e.ReceiptVoucherId).HasColumnName("receipt_voucher_id");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(e => e.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_ledger_entries_type",
            "entry_type IN ('invoice','service_fee','exam_fee','receipt_voucher','adjustment')"));

        // SCHEMA §2: dedupe on sync. UNIQUE per environment so two offline devices retrying the
        // same write collapse to one row.
        builder.HasIndex(e => new { e.EnvironmentId, e.IdempotencyKey })
            .HasDatabaseName("ux_ledger_entries_env_idempotency")
            .IsUnique();

        builder.HasIndex(e => new { e.LedgerId, e.CreatedAt })
            .HasDatabaseName("ix_ledger_entries_ledger");

        // M7 — the polymorphic source FK targets now exist. An invoice/exam-fee entry points at the
        // invoice; a receipt-voucher entry points at the voucher. Both stay nullable (an adjustment
        // entry has neither).
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReceiptVoucher>()
            .WithMany()
            .HasForeignKey(e => e.ReceiptVoucherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
