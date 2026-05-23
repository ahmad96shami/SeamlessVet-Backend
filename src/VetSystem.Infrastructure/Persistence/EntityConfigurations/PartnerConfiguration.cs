using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("partners");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserId).HasColumnName("user_id");
        builder.Property(p => p.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(256);
        builder.Property(p => p.Notes).HasColumnName("notes");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
