using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.ToTable("purchase_invoices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SupplierId).HasColumnName("supplier_id").IsRequired();
        builder.Property(p => p.WarehouseId).HasColumnName("warehouse_id").IsRequired();
        builder.Property(p => p.Number).HasColumnName("number").HasMaxLength(64);
        builder.Property(p => p.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)");
        builder.Property(p => p.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.TaxAmount).HasColumnName("tax_amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.Total).HasColumnName("total").HasColumnType("numeric(14,2)");
        builder.Property(p => p.ReceivedBy).HasColumnName("received_by").IsRequired();
        builder.Property(p => p.ReceivedAt).HasColumnName("received_at");
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.HasIndex(p => new { p.EnvironmentId, p.IdempotencyKey })
            .HasDatabaseName("ux_purchase_invoices_env_idempotency")
            .IsUnique();

        builder.HasIndex(p => new { p.EnvironmentId, p.SupplierId, p.ReceivedAt })
            .HasDatabaseName("ix_purchase_invoices_supplier")
            .IsDescending(false, false, true);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(p => p.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.ReceivedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
