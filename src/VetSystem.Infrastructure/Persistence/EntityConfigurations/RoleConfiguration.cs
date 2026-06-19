using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Key).HasColumnName("key").IsRequired().HasMaxLength(32);
        builder.Property(r => r.Name).HasColumnName("name").IsRequired().HasMaxLength(64);

        // No ck_roles_key CHECK constraint: admins may create custom roles with generated keys
        // (e.g. "custom_xxxxxxxx") beyond the eight built-in RoleKey values. The per-env unique
        // index below still prevents duplicate keys within an environment.

        builder.HasIndex(r => new { r.EnvironmentId, r.Key })
            .HasDatabaseName("ux_roles_env_key")
            .IsUnique();
    }
}
