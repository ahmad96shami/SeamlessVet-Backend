using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.LocationType).HasColumnName("location_type").IsRequired().HasMaxLength(16);
        builder.Property(s => s.LocationId).HasColumnName("location_id").IsRequired();
        builder.Property(s => s.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(s => s.Quantity).HasColumnName("quantity").HasColumnType("numeric(14,3)");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_stock_items_location_type",
            "location_type IN ('warehouse','field')"));

        // SCHEMA §4 — one materialized balance row per (location, product). location_id is
        // polymorphic (warehouses or field_inventories) so it carries no FK; the uniqueness
        // guarantee is what the server relies on for the upsert in IInventoryService.
        builder.HasIndex(s => new { s.LocationType, s.LocationId, s.ProductId })
            .HasDatabaseName("ux_stock_items_location_product")
            .IsUnique();

        builder.HasIndex(s => new { s.EnvironmentId, s.ProductId })
            .HasDatabaseName("ix_stock_items_product");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
