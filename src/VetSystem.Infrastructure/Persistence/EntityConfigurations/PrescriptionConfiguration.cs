using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(p => p.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(p => p.Dosage).HasColumnName("dosage").HasMaxLength(128);
        builder.Property(p => p.Frequency).HasColumnName("frequency").HasMaxLength(128);
        builder.Property(p => p.Duration).HasColumnName("duration").HasMaxLength(128);
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.DispenseType).HasColumnName("dispense_type").IsRequired().HasMaxLength(24);
        builder.Property(p => p.Quantity).HasColumnName("quantity").HasColumnType("numeric(14,3)");

        // M23 — administered_in_clinic meds charged to the customer (assemble into the visit's invoice).
        builder.Property(p => p.Billable).HasColumnName("billable").IsRequired().HasDefaultValue(false);

        // M18 — recurring-dose reminder schedule (only meaningful when reminder_enabled).
        builder.Property(p => p.ReminderEnabled).HasColumnName("reminder_enabled").IsRequired().HasDefaultValue(false);
        builder.Property(p => p.IntervalMinutes).HasColumnName("interval_minutes");
        builder.Property(p => p.LeadMinutes).HasColumnName("lead_minutes");
        builder.Property(p => p.StartAt).HasColumnName("start_at");
        builder.Property(p => p.EndAt).HasColumnName("end_at");
        builder.Property(p => p.DosesCount).HasColumnName("doses_count");
        builder.Property(p => p.LastRemindedDose).HasColumnName("last_reminded_dose");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_prescriptions_dispense_type",
            "dispense_type IN ('administered_in_clinic','dispensed_to_owner')"));

        builder.HasIndex(p => p.VisitId).HasDatabaseName("ix_prescriptions_visit");

        // MedicationDueJob scans only the active-reminder set per environment — a partial index keeps
        // that scan cheap as the prescriptions table grows.
        builder.HasIndex(p => p.EnvironmentId)
            .HasDatabaseName("ix_prescriptions_reminder_active")
            .HasFilter("reminder_enabled = true AND deleted_at IS NULL");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(p => p.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
