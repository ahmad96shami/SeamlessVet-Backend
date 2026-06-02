using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class FarmConfiguration : IEntityTypeConfiguration<Farm>
{
    public void Configure(EntityTypeBuilder<Farm> builder)
    {
        builder.ToTable("farms");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(f => f.Name).HasColumnName("name").IsRequired().HasMaxLength(128);
        builder.Property(f => f.Kind).HasColumnName("kind").IsRequired().HasMaxLength(16);
        builder.Property(f => f.Location).HasColumnName("location");
        builder.Property(f => f.AnimalType).HasColumnName("animal_type").HasMaxLength(64);
        builder.Property(f => f.HeadCount).HasColumnName("head_count");
        builder.Property(f => f.Notes).HasColumnName("notes");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(f => f.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_farms_kind",
            "kind IN ('poultry','cattle','mixed','other')"));

        builder.HasIndex(f => new { f.EnvironmentId, f.CustomerId })
            .HasDatabaseName("ix_farms_customer");
    }
}
