using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ReceiptVoucherConfiguration : IEntityTypeConfiguration<ReceiptVoucher>
{
    public void Configure(EntityTypeBuilder<ReceiptVoucher> builder)
    {
        builder.ToTable("receipt_vouchers");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(v => v.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(v => v.Method).HasColumnName("method").IsRequired().HasMaxLength(16);
        builder.Property(v => v.IssuedBy).HasColumnName("issued_by").IsRequired();
        builder.Property(v => v.IssuedAt).HasColumnName("issued_at");
        builder.Property(v => v.Notes).HasColumnName("notes");
        builder.Property(v => v.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_receipt_vouchers_method", "method IN ('cash','card','bank_transfer','credit')"));

        builder.HasIndex(v => new { v.EnvironmentId, v.IdempotencyKey })
            .HasDatabaseName("ux_receipt_vouchers_env_idempotency")
            .IsUnique();

        builder.HasIndex(v => new { v.EnvironmentId, v.CustomerId, v.IssuedAt })
            .HasDatabaseName("ix_vouchers_customer")
            .IsDescending(false, false, true);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(v => v.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(v => v.IssuedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
