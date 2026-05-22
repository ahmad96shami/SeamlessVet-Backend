using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class SyncTestRecordConfiguration : IEntityTypeConfiguration<SyncTestRecord>
{
    public void Configure(EntityTypeBuilder<SyncTestRecord> builder)
    {
        builder.ToTable("sync_test_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Label).HasColumnName("label").IsRequired().HasMaxLength(128);

        builder.HasIndex(r => new { r.EnvironmentId, r.UpdatedAt })
            .HasDatabaseName("ix_sync_test_records_env_updated");
        builder.HasIndex(r => new { r.EnvironmentId, r.DeletedAt })
            .HasDatabaseName("ix_sync_test_records_env_deleted");
    }
}
