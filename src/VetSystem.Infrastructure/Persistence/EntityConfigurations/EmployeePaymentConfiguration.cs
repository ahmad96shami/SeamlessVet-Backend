using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EmployeePaymentConfiguration : IEntityTypeConfiguration<EmployeePayment>
{
    public void Configure(EntityTypeBuilder<EmployeePayment> builder)
    {
        builder.ToTable("employee_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(p => p.Kind).HasColumnName("kind").IsRequired().HasMaxLength(20);
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.LoanRepaymentAmount).HasColumnName("loan_repayment_amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.Method).HasColumnName("method").IsRequired().HasMaxLength(16);
        builder.Property(p => p.PaidBy).HasColumnName("paid_by").IsRequired();
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.ChequeNumber).HasColumnName("cheque_number").HasMaxLength(64);
        builder.Property(p => p.ChequeBank).HasColumnName("cheque_bank").HasMaxLength(128);
        builder.Property(p => p.ChequeDueDate).HasColumnName("cheque_due_date");
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_employee_payments_kind", "kind IN ('salary_payment','loan','loan_repayment')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_employee_payments_method", "method IN ('cash','card','bank_transfer','cheque')"));

        builder.HasIndex(p => new { p.EnvironmentId, p.IdempotencyKey })
            .HasDatabaseName("ux_employee_payments_env_idempotency")
            .IsUnique();

        builder.HasIndex(p => new { p.EnvironmentId, p.EmployeeId, p.PaidAt })
            .HasDatabaseName("ix_employee_payments_employee")
            .IsDescending(false, false, true);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.PaidBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
