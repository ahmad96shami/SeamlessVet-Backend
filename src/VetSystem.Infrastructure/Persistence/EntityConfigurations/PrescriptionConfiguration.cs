using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(p => p.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(p => p.Dosage).HasColumnName("dosage").HasMaxLength(128);
        builder.Property(p => p.Frequency).HasColumnName("frequency").HasMaxLength(128);
        builder.Property(p => p.Duration).HasColumnName("duration").HasMaxLength(128);
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.DispenseType).HasColumnName("dispense_type").IsRequired().HasMaxLength(24);
        builder.Property(p => p.Quantity).HasColumnName("quantity").HasColumnType("numeric(14,3)");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_prescriptions_dispense_type",
            "dispense_type IN ('administered_in_clinic','dispensed_to_owner')"));

        builder.HasIndex(p => p.VisitId).HasDatabaseName("ix_prescriptions_visit");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(p => p.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
