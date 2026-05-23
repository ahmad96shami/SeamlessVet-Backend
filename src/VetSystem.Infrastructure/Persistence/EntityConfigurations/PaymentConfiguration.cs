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

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
