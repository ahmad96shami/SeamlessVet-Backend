using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasColumnName("name").IsRequired().HasMaxLength(256);
        builder.Property(s => s.PhonePrimary).HasColumnName("phone_primary").HasMaxLength(32);
        builder.Property(s => s.PhoneSecondary).HasColumnName("phone_secondary").HasMaxLength(32);
        builder.Property(s => s.Address).HasColumnName("address");
        builder.Property(s => s.Email).HasColumnName("email").HasMaxLength(256);
        builder.Property(s => s.TaxNumber).HasColumnName("tax_number").HasMaxLength(64);
        builder.Property(s => s.Notes).HasColumnName("notes");

        builder.HasIndex(s => new { s.EnvironmentId, s.Name }).HasDatabaseName("ix_suppliers_name");
    }
}
