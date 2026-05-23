using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.CustomerId).HasColumnName("customer_id");
        builder.Property(a => a.PetId).HasColumnName("pet_id");
        builder.Property(a => a.DoctorId).HasColumnName("doctor_id");
        builder.Property(a => a.ServiceId).HasColumnName("service_id");
        builder.Property(a => a.ScheduledAt).HasColumnName("scheduled_at").IsRequired();
        builder.Property(a => a.DurationMin).HasColumnName("duration_min");
        builder.Property(a => a.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(a => a.Notes).HasColumnName("notes");
        builder.Property(a => a.VisitId).HasColumnName("visit_id");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_appointments_status",
            "status IN ('scheduled','confirmed','attended','no_show','cancelled')"));

        // SCHEMA §7 — the conflict-detection access path: candidate slots for a doctor are scanned
        // by (environment_id, doctor_id) ordered on scheduled_at. Doubles as the calendar index.
        builder.HasIndex(a => new { a.EnvironmentId, a.DoctorId, a.ScheduledAt })
            .HasDatabaseName("ix_appointments_doctor_time");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Pet>()
            .WithMany()
            .HasForeignKey(a => a.PetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(a => a.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // The clinic visit opened on attendance (M6 task 6). Nullable until attended; Restrict so a
        // visit that an appointment points at can't be hard-deleted out from under it.
        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(a => a.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
