using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class BatchSettlementConfiguration : IEntityTypeConfiguration<BatchSettlement>
{
    public void Configure(EntityTypeBuilder<BatchSettlement> builder)
    {
        builder.ToTable("batch_settlements");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(s => s.RepricingDelta).HasColumnName("repricing_delta").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(s => s.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(s => s.OriginalTotal).HasColumnName("original_total").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(s => s.SettledTotal).HasColumnName("settled_total").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(s => s.SupervisionFee).HasColumnName("supervision_fee").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(s => s.Notes).HasColumnName("notes");
        builder.Property(s => s.SettledBy).HasColumnName("settled_by").IsRequired();
        builder.Property(s => s.SettledAt).HasColumnName("settled_at").IsRequired();
        builder.Property(s => s.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_batch_settlements_discount", "discount_amount >= 0"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_batch_settlements_supervision_fee", "supervision_fee >= 0"));

        // One settlement per batch — the concurrency backstop for racing settle calls.
        // Partial on deleted_at so a (manual, admin-level) soft delete can free the slot.
        builder.HasIndex(s => s.BatchId)
            .HasDatabaseName("ux_batch_settlements_batch")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(s => new { s.EnvironmentId, s.IdempotencyKey })
            .HasDatabaseName("ux_batch_settlements_env_idempotency")
            .IsUnique();

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(s => s.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.SettledBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BatchSettlementLineConfiguration : IEntityTypeConfiguration<BatchSettlementLine>
{
    public void Configure(EntityTypeBuilder<BatchSettlementLine> builder)
    {
        builder.ToTable("batch_settlement_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.SettlementId).HasColumnName("settlement_id").IsRequired();
        builder.Property(l => l.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(l => l.SettledUnitPrice).HasColumnName("settled_unit_price").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(l => l.OriginalQuantity).HasColumnName("original_quantity").HasColumnType("numeric(14,3)").IsRequired();
        builder.Property(l => l.OriginalAmount).HasColumnName("original_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(l => l.Delta).HasColumnName("delta").HasColumnType("numeric(14,2)").IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_batch_settlement_lines_price", "settled_unit_price >= 0"));

        // One negotiated price per product per settlement.
        builder.HasIndex(l => new { l.SettlementId, l.ProductId })
            .HasDatabaseName("ux_batch_settlement_lines_product")
            .IsUnique();

        builder.HasOne<BatchSettlement>()
            .WithMany()
            .HasForeignKey(l => l.SettlementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
