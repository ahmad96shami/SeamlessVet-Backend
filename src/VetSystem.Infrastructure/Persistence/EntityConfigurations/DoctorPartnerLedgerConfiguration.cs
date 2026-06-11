using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class DoctorPartnerLedgerConfiguration : IEntityTypeConfiguration<DoctorPartnerLedger>
{
    public void Configure(EntityTypeBuilder<DoctorPartnerLedger> builder)
    {
        builder.ToTable("doctor_partner_ledgers");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.DoctorPartnerId).HasColumnName("doctor_partner_id").IsRequired();
        builder.Property(l => l.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)");
        builder.Property(l => l.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(l => l.ClosedAt).HasColumnName("closed_at");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_doctor_partner_ledgers_status",
            "status IN ('open','has_debt','closed')"));

        // Exactly one ledger per doctor-partner.
        builder.HasIndex(l => l.DoctorPartnerId)
            .HasDatabaseName("ux_doctor_partner_ledgers_partner")
            .IsUnique();

        builder.HasOne<DoctorPartner>()
            .WithMany()
            .HasForeignKey(l => l.DoctorPartnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
