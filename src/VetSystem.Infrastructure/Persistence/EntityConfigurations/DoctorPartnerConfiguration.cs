using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class DoctorPartnerConfiguration : IEntityTypeConfiguration<DoctorPartner>
{
    public void Configure(EntityTypeBuilder<DoctorPartner> builder)
    {
        builder.ToTable("doctor_partners");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.Notes).HasColumnName("notes");

        // One live doctor-partner per staff account (within an environment). Filtered on the
        // soft-delete flag so a deleted partner can be re-created for the same user.
        builder.HasIndex(p => new { p.EnvironmentId, p.UserId })
            .HasDatabaseName("ux_doctor_partners_user")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
