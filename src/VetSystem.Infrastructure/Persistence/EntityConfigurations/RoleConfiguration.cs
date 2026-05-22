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

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_roles_key",
            "key IN ('admin','accountant','vet_clinic','vet_field','vet_both','receptionist','cashier','inventory_staff')"));

        builder.HasIndex(r => new { r.EnvironmentId, r.Key })
            .HasDatabaseName("ux_roles_env_key")
            .IsUnique();
    }
}
