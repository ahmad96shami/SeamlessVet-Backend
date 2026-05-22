using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.UserId).HasColumnName("user_id");
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(64);
        builder.Property(t => t.ReplacedById).HasColumnName("replaced_by_id");

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RefreshToken>().WithMany().HasForeignKey(t => t.ReplacedById).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.UserId, t.ExpiresAt })
            .HasDatabaseName("ix_refresh_tokens_user_expires");

        builder.HasIndex(t => t.TokenHash)
            .HasDatabaseName("ix_refresh_tokens_hash");
    }
}
