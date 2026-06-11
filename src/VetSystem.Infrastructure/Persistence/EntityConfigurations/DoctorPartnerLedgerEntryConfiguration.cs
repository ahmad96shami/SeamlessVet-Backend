using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class DoctorPartnerLedgerEntryConfiguration : IEntityTypeConfiguration<DoctorPartnerLedgerEntry>
{
    public void Configure(EntityTypeBuilder<DoctorPartnerLedgerEntry> builder)
    {
        builder.ToTable("doctor_partner_ledger_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.DoctorPartnerLedgerId).HasColumnName("doctor_partner_ledger_id").IsRequired();
        builder.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired().HasMaxLength(20);
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)");
        builder.Property(e => e.DoctorEntitlementId).HasColumnName("doctor_entitlement_id");
        builder.Property(e => e.BatchId).HasColumnName("batch_id");
        builder.Property(e => e.DoctorPartnerPaymentId).HasColumnName("doctor_partner_payment_id");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_doctor_partner_ledger_entries_type",
            "entry_type IN ('entitlement','payment','adjustment')"));

        // Append-only dedupe: UNIQUE per environment so a retried write collapses to one row.
        builder.HasIndex(e => new { e.EnvironmentId, e.IdempotencyKey })
            .HasDatabaseName("ux_doctor_partner_ledger_entries_env_idempotency")
            .IsUnique();

        builder.HasIndex(e => new { e.DoctorPartnerLedgerId, e.CreatedAt })
            .HasDatabaseName("ix_doctor_partner_ledger_entries_ledger");

        builder.HasOne<DoctorPartnerLedger>()
            .WithMany()
            .HasForeignKey(e => e.DoctorPartnerLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DoctorEntitlement>()
            .WithMany()
            .HasForeignKey(e => e.DoctorEntitlementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DoctorPartnerPayment>()
            .WithMany()
            .HasForeignKey(e => e.DoctorPartnerPaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
