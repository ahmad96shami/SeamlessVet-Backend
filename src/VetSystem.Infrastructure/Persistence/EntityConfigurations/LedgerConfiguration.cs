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
        builder.Property(l => l.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(l => l.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)");
        builder.Property(l => l.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(l => l.ClosedAt).HasColumnName("closed_at");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_ledgers_status",
            "status IN ('open','has_debt','closed')"));

        // SCHEMA §2: exactly one ledger per customer.
        builder.HasIndex(l => l.CustomerId)
            .HasDatabaseName("ux_ledgers_customer")
            .IsUnique();
    }
}
