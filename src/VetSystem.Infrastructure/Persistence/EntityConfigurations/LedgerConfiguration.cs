using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class LedgerConfiguration : IEntityTypeConfiguration<Ledger>
{
    public void Configure(EntityTypeBuilder<Ledger> builder)
    {
        builder.ToTable("ledgers");

        builder.HasKey(l => l.Id);
        // M16: polymorphic owner — customer_id XOR farm_id (ck_ledgers_owner below). Both nullable.
        builder.Property(l => l.CustomerId).HasColumnName("customer_id");
        builder.Property(l => l.FarmId).HasColumnName("farm_id");
        builder.Property(l => l.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)");
        builder.Property(l => l.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(l => l.ClosedAt).HasColumnName("closed_at");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Farm>()
            .WithMany()
            .HasForeignKey(l => l.FarmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_ledgers_status",
            "status IN ('open','has_debt','closed')"));

        // M16: exactly one owner is set (a customer ledger or a farm ledger, never both/neither).
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_ledgers_owner",
            "num_nonnulls(customer_id, farm_id) = 1"));

        // SCHEMA §2 (M16): at most one ledger per owner — two partial unique indexes, one per owner kind.
        builder.HasIndex(l => l.CustomerId)
            .HasDatabaseName("ux_ledgers_customer")
            .IsUnique()
            .HasFilter("customer_id IS NOT NULL");

        builder.HasIndex(l => l.FarmId)
            .HasDatabaseName("ux_ledgers_farm")
            .IsUnique()
            .HasFilter("farm_id IS NOT NULL");
    }
}
