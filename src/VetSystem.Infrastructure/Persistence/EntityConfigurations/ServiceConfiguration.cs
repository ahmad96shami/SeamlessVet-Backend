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
    }
}
