using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class DailyFollowUpConfiguration : IEntityTypeConfiguration<DailyFollowUp>
{
    public void Configure(EntityTypeBuilder<DailyFollowUp> builder)
    {
        builder.ToTable("daily_follow_ups");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(f => f.EntryDate).HasColumnName("entry_date").IsRequired();
        builder.Property(f => f.Condition).HasColumnName("condition");
        builder.Property(f => f.Temperature).HasColumnName("temperature").HasColumnType("numeric(5,2)");
        builder.Property(f => f.HeartRate).HasColumnName("heart_rate");
        builder.Property(f => f.RespiratoryRate).HasColumnName("respiratory_rate");
        builder.Property(f => f.AdministeredMeds).HasColumnName("administered_meds");
        builder.Property(f => f.Notes).HasColumnName("notes");

        builder.HasIndex(f => new { f.VisitId, f.EntryDate }).HasDatabaseName("ix_followups_visit");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(f => f.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
