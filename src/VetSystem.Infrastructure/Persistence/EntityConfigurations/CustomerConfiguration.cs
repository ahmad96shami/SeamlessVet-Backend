using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Type).HasColumnName("type").IsRequired().HasMaxLength(16);
        builder.Property(c => c.FullName).HasColumnName("full_name").IsRequired().HasMaxLength(256);
        builder.Property(c => c.PhonePrimary).HasColumnName("phone_primary").HasMaxLength(32);
        builder.Property(c => c.PhoneSecondary).HasColumnName("phone_secondary").HasMaxLength(32);
        builder.Property(c => c.Address).HasColumnName("address");
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(256);
        builder.Property(c => c.IdNumber).HasColumnName("id_number").HasMaxLength(64);
        builder.Property(c => c.Notes).HasColumnName("notes");
        builder.Property(c => c.AssignedDoctorId).HasColumnName("assigned_doctor_id");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_customers_type",
            "type IN ('regular_farm','home','cattle_farm','poultry_farm')"));

        builder.HasIndex(c => new { c.EnvironmentId, c.PhonePrimary })
            .HasDatabaseName("ux_customers_env_phone")
            .IsUnique()
            .HasFilter("phone_primary IS NOT NULL");

        builder.HasIndex(c => new { c.EnvironmentId, c.AssignedDoctorId })
            .HasDatabaseName("ix_customers_assigned_doctor");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.AssignedDoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
