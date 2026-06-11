using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PurchaseInvoiceItemConfiguration : IEntityTypeConfiguration<PurchaseInvoiceItem>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceItem> builder)
    {
        builder.ToTable("purchase_invoice_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.PurchaseInvoiceId).HasColumnName("purchase_invoice_id").IsRequired();
        builder.Property(i => i.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(i => i.Quantity).HasColumnName("quantity").HasColumnType("numeric(14,3)");
        builder.Property(i => i.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(14,2)");
        builder.Property(i => i.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(14,2)");
        builder.Property(i => i.LineTotal).HasColumnName("line_total").HasColumnType("numeric(14,2)");
        builder.Property(i => i.ExpirationDate).HasColumnName("expiration_date");

        builder.HasIndex(i => i.PurchaseInvoiceId).HasDatabaseName("ix_purchase_invoice_items_invoice");

        builder.HasOne<PurchaseInvoice>()
            .WithMany()
            .HasForeignKey(i => i.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
