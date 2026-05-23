using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class FieldInventoryConfiguration : IEntityTypeConfiguration<FieldInventory>
{
    public void Configure(EntityTypeBuilder<FieldInventory> builder)
    {
        builder.ToTable("field_inventories");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.DoctorId).HasColumnName("doctor_id").IsRequired();

        // SCHEMA §4 — one field inventory per doctor.
        builder.HasIndex(f => f.DoctorId)
            .HasDatabaseName("ux_field_inventories_doctor")
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
