using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class VaccinationConfiguration : IEntityTypeConfiguration<Vaccination>
{
    public void Configure(EntityTypeBuilder<Vaccination> builder)
    {
        builder.ToTable("vaccinations");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.PetId).HasColumnName("pet_id");
        builder.Property(v => v.CustomerId).HasColumnName("customer_id");
        builder.Property(v => v.VisitId).HasColumnName("visit_id");
        builder.Property(v => v.ServiceId).HasColumnName("service_id");
        builder.Property(v => v.VaccineType).HasColumnName("vaccine_type").IsRequired().HasMaxLength(128);
        builder.Property(v => v.Price).HasColumnName("price").HasColumnType("numeric(14,2)");
        builder.Property(v => v.DateGiven).HasColumnName("date_given").IsRequired();
        builder.Property(v => v.NextDueDate).HasColumnName("next_due_date");
        builder.Property(v => v.CertificateUrl).HasColumnName("certificate_url");

        // SCHEMA §6 — drives the M11 reminder job; filtered to rows that actually have a due date.
        builder.HasIndex(v => new { v.EnvironmentId, v.NextDueDate })
            .HasDatabaseName("ix_vaccinations_due")
            .HasFilter("next_due_date IS NOT NULL");

        builder.HasOne<Pet>()
            .WithMany()
            .HasForeignKey(v => v.PetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(v => v.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(v => v.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(v => v.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
