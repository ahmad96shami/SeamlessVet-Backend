using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(m => m.MovementType).HasColumnName("movement_type").IsRequired().HasMaxLength(20);
        builder.Property(m => m.FromLocationType).HasColumnName("from_location_type").HasMaxLength(16);
        builder.Property(m => m.FromLocationId).HasColumnName("from_location_id");
        builder.Property(m => m.ToLocationType).HasColumnName("to_location_type").HasMaxLength(16);
        builder.Property(m => m.ToLocationId).HasColumnName("to_location_id");
        builder.Property(m => m.QuantityDelta).HasColumnName("quantity_delta").HasColumnType("numeric(14,3)");
        builder.Property(m => m.Reason).HasColumnName("reason");
        builder.Property(m => m.VisitId).HasColumnName("visit_id");
        builder.Property(m => m.InvoiceId).HasColumnName("invoice_id");
        builder.Property(m => m.PerformedBy).HasColumnName("performed_by").IsRequired();
        builder.Property(m => m.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_movements_type",
            "movement_type IN ('receive','adjust','load_to_field','unload_from_field','sale_deduct','return_add')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_movements_from_location_type",
            "from_location_type IS NULL OR from_location_type IN ('warehouse','field')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_movements_to_location_type",
            "to_location_type IS NULL OR to_location_type IN ('warehouse','field')"));

        // SCHEMA §4 — dedupe on sync. UNIQUE per environment so retried offline writes collapse.
        builder.HasIndex(m => new { m.EnvironmentId, m.IdempotencyKey })
            .HasDatabaseName("ux_inventory_movements_env_idempotency")
            .IsUnique();

        builder.HasIndex(m => new { m.EnvironmentId, m.ProductId, m.CreatedAt })
            .HasDatabaseName("ix_inv_moves_product_time");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.PerformedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Note: visit_id / invoice_id FK targets are added by M5 / M7 when those tables come
        // online — the columns exist here so the source can be set as soon as those endpoints post.
    }
}
