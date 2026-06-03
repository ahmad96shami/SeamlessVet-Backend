using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class SupplierLedgerEntryConfiguration : IEntityTypeConfiguration<SupplierLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplierLedgerEntry> builder)
    {
        builder.ToTable("supplier_ledger_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SupplierLedgerId).HasColumnName("supplier_ledger_id").IsRequired();
        builder.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired().HasMaxLength(20);
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)");
        builder.Property(e => e.PurchaseInvoiceId).HasColumnName("purchase_invoice_id");
        builder.Property(e => e.SupplierPaymentId).HasColumnName("supplier_payment_id");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_supplier_ledger_entries_type",
            "entry_type IN ('purchase_invoice','payment','adjustment')"));

        // Append-only dedupe: UNIQUE per environment so a retried write collapses to one row.
        builder.HasIndex(e => new { e.EnvironmentId, e.IdempotencyKey })
            .HasDatabaseName("ux_supplier_ledger_entries_env_idempotency")
            .IsUnique();

        builder.HasIndex(e => new { e.SupplierLedgerId, e.CreatedAt })
            .HasDatabaseName("ix_supplier_ledger_entries_ledger");

        builder.HasOne<SupplierLedger>()
            .WithMany()
            .HasForeignKey(e => e.SupplierLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PurchaseInvoice>()
            .WithMany()
            .HasForeignKey(e => e.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SupplierPayment>()
            .WithMany()
            .HasForeignKey(e => e.SupplierPaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
