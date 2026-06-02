using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.ContractId).HasColumnName("contract_id");
        builder.Property(b => b.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(b => b.FarmId).HasColumnName("farm_id");
        builder.Property(b => b.ResponsibleDoctorId).HasColumnName("responsible_doctor_id").IsRequired();
        builder.Property(b => b.AnimalCount).HasColumnName("animal_count").IsRequired();
        builder.Property(b => b.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(b => b.EndDate).HasColumnName("end_date");
        builder.Property(b => b.SupervisionFeeModel).HasColumnName("supervision_fee_model").IsRequired().HasMaxLength(24);
        builder.Property(b => b.SupervisionFeeValue).HasColumnName("supervision_fee_value").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(b => b.EntitlementEnabled).HasColumnName("entitlement_enabled");
        builder.Property(b => b.EntitlementSystem).HasColumnName("entitlement_system").HasMaxLength(16);
        builder.Property(b => b.DoctorSharePercent).HasColumnName("doctor_share_percent").HasColumnType("numeric(5,2)");
        builder.Property(b => b.DoctorShareCeiling).HasColumnName("doctor_share_ceiling").HasColumnType("numeric(14,2)");
        builder.Property(b => b.Status).HasColumnName("status").IsRequired().HasMaxLength(16);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_batches_fee_model",
            "supervision_fee_model IN ('fixed_amount','percent_of_invoice','per_bird','per_batch_fixed')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_batches_entitlement_system",
            "entitlement_system IS NULL OR entitlement_system IN ('drug_profit','direct_fee')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_batches_status", "status IN ('open','closed')"));

        builder.HasIndex(b => new { b.EnvironmentId, b.ResponsibleDoctorId })
            .HasDatabaseName("ix_batches_doctor");
        builder.HasIndex(b => new { b.EnvironmentId, b.CustomerId })
            .HasDatabaseName("ix_batches_customer");
        builder.HasIndex(b => new { b.EnvironmentId, b.FarmId })
            .HasDatabaseName("ix_batches_farm");

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(b => b.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // M15 — the cycle's farm (denormalized customer_id mirrors farm.customer_id).
        builder.HasOne<Farm>()
            .WithMany()
            .HasForeignKey(b => b.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.ResponsibleDoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
