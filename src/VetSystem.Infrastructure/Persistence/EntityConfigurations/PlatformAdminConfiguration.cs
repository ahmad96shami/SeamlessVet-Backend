using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// M35 — <c>platform_admins</c> is a plain POCO (no <c>Entity</c> base), so every column is mapped
/// here explicitly: the shared conventions (audit stamps, env filter, xmin) do not apply. The table
/// is global (no <c>environment_id</c>) and is excluded from <c>DataSeeder.ClearAsync</c>.
/// </summary>
internal sealed class PlatformAdminConfiguration : IEntityTypeConfiguration<PlatformAdmin>
{
    public void Configure(EntityTypeBuilder<PlatformAdmin> builder)
    {
        builder.ToTable("platform_admins");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.FullName).HasColumnName("full_name").IsRequired().HasMaxLength(200);
        builder.Property(p => p.Phone).HasColumnName("phone").IsRequired().HasMaxLength(32);
        builder.Property(p => p.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // Login identifier is globally unique (the seeder + console enforce it on create).
        builder.HasIndex(p => p.Phone)
            .HasDatabaseName("ux_platform_admins_phone")
            .IsUnique();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_platform_admins_status", "status IN ('active','suspended')"));
    }
}
