using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasColumnName("key").IsRequired().HasMaxLength(64);
        builder.Property(p => p.Description).HasColumnName("description");

        builder.HasIndex(p => new { p.EnvironmentId, p.Key })
            .HasDatabaseName("ux_permissions_env_key")
            .IsUnique();
    }
}
