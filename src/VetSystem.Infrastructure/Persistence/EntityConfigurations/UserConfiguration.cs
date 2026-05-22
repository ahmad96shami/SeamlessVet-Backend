using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.RoleId).HasColumnName("role_id");
        builder.Property(u => u.FullName).HasColumnName("full_name").IsRequired().HasMaxLength(128);
        builder.Property(u => u.PhonePrimary).HasColumnName("phone_primary").IsRequired().HasMaxLength(32);
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(u => u.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(u => u.NumberPrefix).HasColumnName("number_prefix").HasMaxLength(8);
        builder.Property(u => u.LicenseNumber).HasColumnName("license_number").HasMaxLength(64);
        builder.Property(u => u.LicenseDetails).HasColumnName("license_details").HasColumnType("jsonb");

        builder.HasOne<Role>().WithMany().HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_users_status",
            "status IN ('inactive','active','suspended')"));

        builder.HasIndex(u => new { u.EnvironmentId, u.PhonePrimary })
            .HasDatabaseName("ux_users_env_phone")
            .IsUnique();

        builder.HasIndex(u => new { u.EnvironmentId, u.NumberPrefix })
            .HasDatabaseName("ux_users_env_number_prefix")
            .IsUnique()
            .HasFilter("number_prefix IS NOT NULL");
    }
}
