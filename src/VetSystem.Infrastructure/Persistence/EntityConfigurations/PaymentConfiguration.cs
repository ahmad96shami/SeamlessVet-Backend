using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.Property(p => p.Method).HasColumnName("method").IsRequired().HasMaxLength(16);
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_payments_method", "method IN ('cash','card','bank_transfer','credit')"));

        builder.HasIndex(p => p.InvoiceId).HasDatabaseName("ix_payments_invoice");

        // M14 — denormalized scope keys for PowerSync's by_customer / by_visit buckets (single-table
        // data queries can't JOIN to invoices). Shadow properties; a BEFORE INSERT trigger copies the
        // parent invoice's customer_id + visit_id (migration M14_SyncScopeDenormalization). payments are
        // append-only — insert-time population suffices.
        builder.Property<Guid?>("CustomerId").HasColumnName("customer_id");
        builder.Property<Guid?>("VisitId").HasColumnName("visit_id");
        builder.HasIndex("CustomerId").HasDatabaseName("ix_payments_customer");
        builder.HasIndex("VisitId").HasDatabaseName("ix_payments_visit");

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
