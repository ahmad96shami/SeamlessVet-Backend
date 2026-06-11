using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EmployeeLedgerConfiguration : IEntityTypeConfiguration<EmployeeLedger>
{
    public void Configure(EntityTypeBuilder<EmployeeLedger> builder)
    {
        builder.ToTable("employee_ledgers");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(l => l.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)");
        builder.Property(l => l.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(l => l.ClosedAt).HasColumnName("closed_at");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_employee_ledgers_status",
            "status IN ('open','has_debt','closed')"));

        // Exactly one ledger per employee.
        builder.HasIndex(l => l.EmployeeId)
            .HasDatabaseName("ux_employee_ledgers_employee")
            .IsUnique();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(l => l.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
