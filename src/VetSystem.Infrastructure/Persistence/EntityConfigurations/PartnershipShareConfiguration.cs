using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PartnershipShareConfiguration : IEntityTypeConfiguration<PartnershipShare>
{
    public void Configure(EntityTypeBuilder<PartnershipShare> builder)
    {
        builder.ToTable("partnership_shares");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.PartnerId).HasColumnName("partner_id").IsRequired();
        builder.Property(s => s.SharePercent).HasColumnName("share_percent").HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(s => s.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(s => s.EffectiveTo).HasColumnName("effective_to");

        // The 0..100 per-row bound is a hard table constraint; the cross-row "active shares sum to
        // ≤ 100 on every effective date" invariant cannot be expressed as a column CHECK and is
        // enforced by IPartnershipValidator in the service layer (SCHEMA §1).
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_partnership_shares_percent", "share_percent >= 0 AND share_percent <= 100"));

        builder.HasIndex(s => new { s.EnvironmentId, s.PartnerId })
            .HasDatabaseName("ix_partnership_shares_partner");

        builder.HasOne<Partner>()
            .WithMany()
            .HasForeignKey(s => s.PartnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
