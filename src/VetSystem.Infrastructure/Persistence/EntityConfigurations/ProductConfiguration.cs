using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.NameAr).HasColumnName("name_ar").IsRequired().HasMaxLength(256);
        builder.Property(p => p.NameLatin).HasColumnName("name_latin").HasMaxLength(256);
        builder.Property(p => p.Barcode).HasColumnName("barcode").HasMaxLength(64);
        builder.Property(p => p.Category).HasColumnName("category").IsRequired().HasMaxLength(16);
        builder.Property(p => p.Manufacturer).HasColumnName("manufacturer").HasMaxLength(128);
        builder.Property(p => p.Supplier).HasColumnName("supplier").HasMaxLength(128);
        builder.Property(p => p.PurchasePrice).HasColumnName("purchase_price").HasColumnType("numeric(14,2)");
        builder.Property(p => p.SellingPrice).HasColumnName("selling_price").HasColumnType("numeric(14,2)");
        builder.Property(p => p.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(32);
        builder.Property(p => p.ExpirationDate).HasColumnName("expiration_date");
        builder.Property(p => p.ReorderPoint).HasColumnName("reorder_point").HasColumnType("numeric(14,3)");
        builder.Property(p => p.IsConsumable).HasColumnName("is_consumable").HasDefaultValue(false);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_products_category",
            "category IN ('medication','product','vaccine')"));

        // Barcodes are intentionally NON-unique: the same code may be shared by several products
        // (the POS resolves a scan to every match and lets the cashier pick). This is a plain lookup
        // index over LIVE, barcoded rows (the POS/admin barcode searches), not a uniqueness constraint.
        builder.HasIndex(p => new { p.EnvironmentId, p.Barcode })
            .HasDatabaseName("ix_products_env_barcode")
            .HasFilter("barcode IS NOT NULL AND deleted_at IS NULL");
    }
}
