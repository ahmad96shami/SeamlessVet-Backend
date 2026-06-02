using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PetConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> builder)
    {
        builder.ToTable("pets");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(p => p.FarmId).HasColumnName("farm_id");
        builder.Property(p => p.Name).HasColumnName("name").IsRequired().HasMaxLength(128);
        builder.Property(p => p.Species).HasColumnName("species").HasMaxLength(64);
        builder.Property(p => p.Breed).HasColumnName("breed").HasMaxLength(128);
        builder.Property(p => p.Sex).HasColumnName("sex").HasMaxLength(8);
        builder.Property(p => p.DateOfBirth).HasColumnName("date_of_birth");
        builder.Property(p => p.ColorMarks).HasColumnName("color_marks");
        builder.Property(p => p.WeightLatest).HasColumnName("weight_latest").HasColumnType("numeric(8,3)");
        builder.Property(p => p.PhotoUrl).HasColumnName("photo_url");
        builder.Property(p => p.MicrochipNo).HasColumnName("microchip_no").HasMaxLength(64);
        builder.Property(p => p.HealthNotes).HasColumnName("health_notes");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // M15 — optional farm membership (within the same customer).
        builder.HasOne<Farm>()
            .WithMany()
            .HasForeignKey(p => p.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_pets_sex",
            "sex IS NULL OR sex IN ('male','female','unknown')"));

        builder.HasIndex(p => new { p.EnvironmentId, p.CustomerId })
            .HasDatabaseName("ix_pets_customer");
    }
}
