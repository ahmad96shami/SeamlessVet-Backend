using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class SupplierLedgerConfiguration : IEntityTypeConfiguration<SupplierLedger>
{
    public void Configure(EntityTypeBuilder<SupplierLedger> builder)
    {
        builder.ToTable("supplier_ledgers");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.SupplierId).HasColumnName("supplier_id").IsRequired();
        builder.Property(l => l.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)");
        builder.Property(l => l.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(l => l.ClosedAt).HasColumnName("closed_at");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_supplier_ledgers_status",
            "status IN ('open','has_debt','closed')"));

        // Exactly one ledger per supplier.
        builder.HasIndex(l => l.SupplierId)
            .HasDatabaseName("ux_supplier_ledgers_supplier")
            .IsUnique();

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(l => l.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
