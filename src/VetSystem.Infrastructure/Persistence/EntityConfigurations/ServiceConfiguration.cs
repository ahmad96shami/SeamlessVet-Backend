using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.NameAr).HasColumnName("name_ar").IsRequired().HasMaxLength(256);
        builder.Property(s => s.NameLatin).HasColumnName("name_latin").HasMaxLength(256);
        builder.Property(s => s.Category).HasColumnName("category").HasMaxLength(32);
        builder.Property(s => s.DefaultPrice).HasColumnName("default_price").HasColumnType("numeric(14,2)");

        // M23 — the checkup-fee / night-stay system services are find-or-created at issuance; this
        // partial unique index turns a concurrent double-create into a catchable unique violation.
        // Scoped to the system categories so admin-defined categories stay free-form.
        builder.HasIndex(s => new { s.EnvironmentId, s.Category })
            .HasDatabaseName("ux_services_system_category")
            .IsUnique()
            .HasFilter($"category IN ('{ServiceCategories.Checkup}','{ServiceCategories.NightStay}') AND deleted_at IS NULL");
    }
}
