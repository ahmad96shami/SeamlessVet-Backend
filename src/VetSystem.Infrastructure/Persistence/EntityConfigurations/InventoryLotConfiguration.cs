using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> builder)
    {
        builder.ToTable("inventory_lots");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(l => l.LocationType).HasColumnName("location_type").IsRequired().HasMaxLength(16);
        builder.Property(l => l.LocationId).HasColumnName("location_id").IsRequired();
        builder.Property(l => l.PurchaseInvoiceItemId).HasColumnName("purchase_invoice_item_id");
        builder.Property(l => l.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(14,2)");
        builder.Property(l => l.ExpirationDate).HasColumnName("expiration_date");
        builder.Property(l => l.LotNumber).HasColumnName("lot_number").HasMaxLength(64);
        builder.Property(l => l.ReceivedQty).HasColumnName("received_qty").HasColumnType("numeric(14,3)");
        builder.Property(l => l.RemainingQty).HasColumnName("remaining_qty").HasColumnType("numeric(14,3)");
        builder.Property(l => l.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_lots_location_type",
            "location_type IN ('warehouse','field')"));

        // FEFO query path: by (location, product), only still-on-hand lots, earliest expiry first.
        builder.HasIndex(l => new { l.LocationType, l.LocationId, l.ProductId, l.ExpirationDate })
            .HasDatabaseName("ix_inventory_lots_fefo")
            .HasFilter("remaining_qty > 0 AND deleted_at IS NULL");

        builder.HasIndex(l => new { l.EnvironmentId, l.ProductId })
            .HasDatabaseName("ix_inventory_lots_product");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // location_id is polymorphic (warehouses or field_inventories) so it carries no FK, mirroring StockItem.
        builder.HasOne<PurchaseInvoiceItem>()
            .WithMany()
            .HasForeignKey(l => l.PurchaseInvoiceItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
