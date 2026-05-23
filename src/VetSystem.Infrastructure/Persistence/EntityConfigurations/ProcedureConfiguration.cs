using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ProcedureConfiguration : IEntityTypeConfiguration<Procedure>
{
    public void Configure(EntityTypeBuilder<Procedure> builder)
    {
        builder.ToTable("procedures");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(p => p.ServiceId).HasColumnName("service_id");
        builder.Property(p => p.ResultText).HasColumnName("result_text");
        builder.Property(p => p.ResultFileUrl).HasColumnName("result_file_url");
        builder.Property(p => p.Price).HasColumnName("price").HasColumnType("numeric(14,2)");

        builder.HasIndex(p => p.VisitId).HasDatabaseName("ix_procedures_visit");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(p => p.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(p => p.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
