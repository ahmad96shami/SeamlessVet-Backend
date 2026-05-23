using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(c => c.ResponsibleDoctorId).HasColumnName("responsible_doctor_id");
        builder.Property(c => c.PeriodStart).HasColumnName("period_start").IsRequired();
        builder.Property(c => c.PeriodEnd).HasColumnName("period_end");
        builder.Property(c => c.TotalPrice).HasColumnName("total_price").HasColumnType("numeric(14,2)");
        builder.Property(c => c.ExpectedVisitCount).HasColumnName("expected_visit_count");
        builder.Property(c => c.AnimalType).HasColumnName("animal_type").HasMaxLength(64);
        builder.Property(c => c.AnimalCount).HasColumnName("animal_count");
        builder.Property(c => c.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.ActivatedBy).HasColumnName("activated_by");
        builder.Property(c => c.ActivatedAt).HasColumnName("activated_at");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_contracts_status", "status IN ('draft','active','completed','cancelled')"));

        builder.HasIndex(c => new { c.EnvironmentId, c.CustomerId })
            .HasDatabaseName("ix_contracts_customer");
        builder.HasIndex(c => new { c.EnvironmentId, c.ResponsibleDoctorId })
            .HasDatabaseName("ix_contracts_doctor");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.ResponsibleDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.ActivatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
