using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class DoctorPartnerPaymentConfiguration : IEntityTypeConfiguration<DoctorPartnerPayment>
{
    public void Configure(EntityTypeBuilder<DoctorPartnerPayment> builder)
    {
        builder.ToTable("doctor_partner_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.DoctorPartnerId).HasColumnName("doctor_partner_id").IsRequired();
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.Method).HasColumnName("method").IsRequired().HasMaxLength(16);
        builder.Property(p => p.PaidBy).HasColumnName("paid_by").IsRequired();
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.ChequeNumber).HasColumnName("cheque_number").HasMaxLength(64);
        builder.Property(p => p.ChequeBank).HasColumnName("cheque_bank").HasMaxLength(128);
        builder.Property(p => p.ChequeDueDate).HasColumnName("cheque_due_date");
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_doctor_partner_payments_method", "method IN ('cash','card','bank_transfer','cheque')"));

        builder.HasIndex(p => new { p.EnvironmentId, p.IdempotencyKey })
            .HasDatabaseName("ux_doctor_partner_payments_env_idempotency")
            .IsUnique();

        builder.HasIndex(p => new { p.EnvironmentId, p.DoctorPartnerId, p.PaidAt })
            .HasDatabaseName("ix_doctor_partner_payments_partner")
            .IsDescending(false, false, true);

        builder.HasOne<DoctorPartner>()
            .WithMany()
            .HasForeignKey(p => p.DoctorPartnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.PaidBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
