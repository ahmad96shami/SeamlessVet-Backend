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
        builder.Property(v => v.ProductId).HasColumnName("product_id");
        builder.Property(v => v.VaccineType).HasColumnName("vaccine_type").IsRequired().HasMaxLength(128);
        builder.Property(v => v.Price).HasColumnName("price").HasColumnType("numeric(14,2)");
        builder.Property(v => v.ResolvedUnitCost).HasColumnName("resolved_unit_cost").HasColumnType("numeric(14,2)");
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

        // M26 — the catalog vaccine is now a stock product (category vaccine).
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
