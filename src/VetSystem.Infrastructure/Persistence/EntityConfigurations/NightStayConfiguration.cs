using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class NightStayConfiguration : IEntityTypeConfiguration<NightStay>
{
    public void Configure(EntityTypeBuilder<NightStay> builder)
    {
        builder.ToTable("night_stays");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(n => n.CareType).HasColumnName("care_type").IsRequired().HasMaxLength(16);
        builder.Property(n => n.CheckInAt).HasColumnName("check_in_at").IsRequired();
        builder.Property(n => n.CheckOutAt).HasColumnName("check_out_at");
        builder.Property(n => n.NightsCount).HasColumnName("nights_count");
        builder.Property(n => n.NightlyRate).HasColumnName("nightly_rate").HasColumnType("numeric(14,2)");
        builder.Property(n => n.Total).HasColumnName("total").HasColumnType("numeric(14,2)");
        builder.Property(n => n.ExitHour).HasColumnName("exit_hour");
        builder.Property(n => n.Notes).HasColumnName("notes");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_night_stays_care_type", "care_type IN ('medical','icu','hotel')"));

        builder.HasIndex(n => new { n.VisitId, n.CheckInAt }).HasDatabaseName("ix_night_stays_visit");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(n => n.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
