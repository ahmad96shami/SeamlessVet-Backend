using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class DoctorEntitlementConfiguration : IEntityTypeConfiguration<DoctorEntitlement>
{
    public void Configure(EntityTypeBuilder<DoctorEntitlement> builder)
    {
        builder.ToTable("doctor_entitlements");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.DoctorId).HasColumnName("doctor_id").IsRequired();
        builder.Property(e => e.BatchId).HasColumnName("batch_id");
        builder.Property(e => e.VisitId).HasColumnName("visit_id");
        builder.Property(e => e.CalculationSystem).HasColumnName("calculation_system").IsRequired().HasMaxLength(16);
        builder.Property(e => e.ComputedAmount).HasColumnName("computed_amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(e => e.ApprovedBy).HasColumnName("approved_by");
        builder.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        builder.Property(e => e.PaidAt).HasColumnName("paid_at");
        builder.Property(e => e.PaidMethod).HasColumnName("paid_method").HasMaxLength(16);

        // SCHEMA §8 — exactly one source: a batch (Dawra) or a single visit, never both/neither.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_entitlement_source",
            "(batch_id IS NOT NULL AND visit_id IS NULL) OR (batch_id IS NULL AND visit_id IS NOT NULL)"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_entitlements_system",
            "calculation_system IN ('drug_profit','direct_fee')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_entitlements_status",
            "status IN ('pending','approved','paid')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_entitlements_paid_method",
            "paid_method IS NULL OR paid_method IN ('cash','card','bank_transfer','credit')"));

        builder.HasIndex(e => new { e.EnvironmentId, e.DoctorId, e.Status })
            .HasDatabaseName("ix_entitlements_doctor_status");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(e => e.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ApprovedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
