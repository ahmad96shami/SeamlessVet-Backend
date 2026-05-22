using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class RegistrationRequestConfiguration : IEntityTypeConfiguration<RegistrationRequest>
{
    public void Configure(EntityTypeBuilder<RegistrationRequest> builder)
    {
        builder.ToTable("registration_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.RequestedRoleKey).HasColumnName("requested_role_key").IsRequired().HasMaxLength(32);
        builder.Property(r => r.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(r => r.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(r => r.ReviewNotes).HasColumnName("review_notes");

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReviewedBy).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_registration_requests_status",
            "status IN ('pending','approved','rejected')"));

        builder.HasIndex(r => new { r.EnvironmentId, r.Status })
            .HasDatabaseName("ix_registration_requests_env_status");
    }
}
