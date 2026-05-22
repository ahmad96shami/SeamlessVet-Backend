using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(k => k.Key);
        builder.Property(k => k.Key).HasColumnName("key").HasMaxLength(128);
        builder.Property(k => k.EnvironmentId).HasColumnName("environment_id");
        builder.Property(k => k.EntityType).HasColumnName("entity_type").IsRequired().HasMaxLength(64);
        builder.Property(k => k.ResultRef).HasColumnName("result_ref");
        builder.Property(k => k.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(k => new { k.EnvironmentId, k.Key })
            .HasDatabaseName("ix_idempotency_keys_env_key")
            .IsUnique();
    }
}
