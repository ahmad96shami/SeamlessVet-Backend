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
        builder.Property(e => e.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(e => e.CalculationSystem).HasColumnName("calculation_system").IsRequired().HasMaxLength(16);
        builder.Property(e => e.ComputedAmount).HasColumnName("computed_amount").HasColumnType("numeric(14,2)").IsRequired();

        // M30 — batch-only (the per-visit source + the approve/pay lifecycle were removed); the row is an
        // immutable accrual audit, credited to the doctor-partner ledger on settlement.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_entitlements_system",
            "calculation_system IN ('drug_profit','direct_fee')"));

        builder.HasIndex(e => new { e.EnvironmentId, e.DoctorId })
            .HasDatabaseName("ix_entitlements_doctor");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
