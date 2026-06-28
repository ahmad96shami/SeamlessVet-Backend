using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EmployeeLedgerEntryConfiguration : IEntityTypeConfiguration<EmployeeLedgerEntry>
{
    public void Configure(EntityTypeBuilder<EmployeeLedgerEntry> builder)
    {
        builder.ToTable("employee_ledger_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeLedgerId).HasColumnName("employee_ledger_id").IsRequired();
        builder.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired().HasMaxLength(20);
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)");
        builder.Property(e => e.EmployeePaymentId).HasColumnName("employee_payment_id");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_employee_ledger_entries_type",
            "entry_type IN ('salary_accrual','salary_payment','loan','loan_repayment','adjustment','deduction')"));

        // Append-only dedupe: UNIQUE per environment so a retried write — including the monthly accrual's
        // period key (salary-accrual-{employeeId}-{yyyyMM}) — collapses to one row.
        builder.HasIndex(e => new { e.EnvironmentId, e.IdempotencyKey })
            .HasDatabaseName("ux_employee_ledger_entries_env_idempotency")
            .IsUnique();

        builder.HasIndex(e => new { e.EmployeeLedgerId, e.CreatedAt })
            .HasDatabaseName("ix_employee_ledger_entries_ledger");

        builder.HasOne<EmployeeLedger>()
            .WithMany()
            .HasForeignKey(e => e.EmployeeLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EmployeePayment>()
            .WithMany()
            .HasForeignKey(e => e.EmployeePaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
