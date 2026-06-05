using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// M21 — <c>device_tokens</c> is a plain POCO (no <c>Entity</c> base), so every column is mapped
/// here explicitly: the shared conventions (audit stamps, env filter, xmin) do not apply.
/// </summary>
internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(d => d.EnvironmentId).HasColumnName("environment_id").IsRequired();
        builder.Property(d => d.Token).HasColumnName("token").IsRequired().HasMaxLength(512);
        builder.Property(d => d.Platform).HasColumnName("platform").IsRequired().HasMaxLength(16);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.LastSeenAt).HasColumnName("last_seen_at");

        // One physical device = one row, whoever is signed in (register upserts by token).
        builder.HasIndex(d => d.Token)
            .HasDatabaseName("ux_device_tokens_token")
            .IsUnique();

        // Unregister-on-logout and the push fan-out both look tokens up by owner.
        builder.HasIndex(d => d.UserId)
            .HasDatabaseName("ix_device_tokens_user");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_device_tokens_platform", "platform IN ('android','ios')"));

        // Cascade like refresh_tokens: a deleted user's device tokens are meaningless.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
