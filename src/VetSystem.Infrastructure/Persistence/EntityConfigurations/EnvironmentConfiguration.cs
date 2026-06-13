using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EnvironmentConfiguration : IEntityTypeConfiguration<DomainEnvironment>
{
    public void Configure(EntityTypeBuilder<DomainEnvironment> builder)
    {
        builder.ToTable("environments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").IsRequired().HasMaxLength(32);
        builder.Property(e => e.Mode).HasColumnName("mode").IsRequired().HasMaxLength(32);
        builder.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.Property(e => e.Xmin)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_environments_mode",
            "mode IN ('solo','partnership')"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_environments_status",
            "status IN ('active','suspended')"));

        // Human-friendly tenant code is globally unique (the platform console enforces it on create).
        builder.HasIndex(e => e.Code).HasDatabaseName("ux_environments_code").IsUnique();

        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
