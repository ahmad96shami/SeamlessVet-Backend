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

        // M34 — refresh/logout look up by hash alone (env read off the row), so the hash must be
        // provably unique. The deterministic SHA-256 of a 256-bit random token is unique by
        // construction; the unique index makes that a hard guarantee.
        builder.HasIndex(t => t.TokenHash)
            .HasDatabaseName("ix_refresh_tokens_hash")
            .IsUnique();
    }
}
