using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visits");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.VisitType).HasColumnName("visit_type").IsRequired().HasMaxLength(16);
        builder.Property(v => v.VisitNumber).HasColumnName("visit_number").HasMaxLength(64);
        builder.Property(v => v.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(v => v.PetId).HasColumnName("pet_id");
        builder.Property(v => v.BatchId).HasColumnName("batch_id");
        builder.Property(v => v.ContractId).HasColumnName("contract_id");
        builder.Property(v => v.DoctorId).HasColumnName("doctor_id").IsRequired();
        builder.Property(v => v.ReceptionistId).HasColumnName("receptionist_id");
        builder.Property(v => v.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(v => v.StartedAt).HasColumnName("started_at");
        builder.Property(v => v.EndedAt).HasColumnName("ended_at");

        builder.Property(v => v.ChiefComplaint).HasColumnName("chief_complaint");
        builder.Property(v => v.Symptoms).HasColumnName("symptoms");
        builder.Property(v => v.Temperature).HasColumnName("temperature").HasColumnType("numeric(5,2)");
        builder.Property(v => v.HeartRate).HasColumnName("heart_rate");
        builder.Property(v => v.RespiratoryRate).HasColumnName("respiratory_rate");
        builder.Property(v => v.Weight).HasColumnName("weight").HasColumnType("numeric(8,3)");
        builder.Property(v => v.ClinicalNotes).HasColumnName("clinical_notes");

        builder.Property(v => v.PreliminaryDiagnosis).HasColumnName("preliminary_diagnosis");
        builder.Property(v => v.FinalDiagnosis).HasColumnName("final_diagnosis");
        builder.Property(v => v.Severity).HasColumnName("severity").HasMaxLength(16);
        builder.Property(v => v.IcdVetCode).HasColumnName("icd_vet_code").HasMaxLength(32);
        builder.Property(v => v.ExamFeeApplied).HasColumnName("exam_fee_applied").HasColumnType("numeric(14,2)");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_visits_type", "visit_type IN ('in_clinic','field')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_visits_status", "status IN ('open','in_progress','completed','cancelled')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_visits_severity",
            "severity IS NULL OR severity IN ('mild','moderate','severe','critical')"));

        // SCHEMA §6 — visit_number is per-user-prefixed and client-generated; UNIQUE per environment
        // (offline-safe). It is nullable, and Postgres treats NULLs as distinct, so visits without a
        // number (e.g. an appointment shell) don't collide.
        builder.HasIndex(v => new { v.EnvironmentId, v.VisitNumber })
            .HasDatabaseName("ux_visits_env_number")
            .IsUnique();

        builder.HasIndex(v => new { v.EnvironmentId, v.CustomerId, v.StartedAt })
            .HasDatabaseName("ix_visits_customer_time")
            .IsDescending(false, false, true);
        builder.HasIndex(v => new { v.EnvironmentId, v.DoctorId, v.StartedAt })
            .HasDatabaseName("ix_visits_doctor_time")
            .IsDescending(false, false, true);
        builder.HasIndex(v => new { v.EnvironmentId, v.BatchId })
            .HasDatabaseName("ix_visits_batch");
        builder.HasIndex(v => new { v.EnvironmentId, v.ContractId })
            .HasDatabaseName("ix_visits_contract");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(v => v.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Pet>()
            .WithMany()
            .HasForeignKey(v => v.PetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(v => v.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(v => v.ReceptionistId)
            .OnDelete(DeleteBehavior.Restrict);

        // batch_id / contract_id FK targets land in M8 — the columns exist now so field visits can
        // carry them as soon as those tables come online.
    }
}
