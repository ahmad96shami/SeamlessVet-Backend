using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class OperatingExpenseConfiguration : IEntityTypeConfiguration<OperatingExpense>
{
    public void Configure(EntityTypeBuilder<OperatingExpense> builder)
    {
        builder.ToTable("operating_expenses");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Category).HasColumnName("category").IsRequired().HasMaxLength(32);
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        builder.Property(e => e.IncurredOn).HasColumnName("incurred_on");
        builder.Property(e => e.Paid).HasColumnName("paid");
        builder.Property(e => e.PaidAt).HasColumnName("paid_at");
        builder.Property(e => e.Note).HasColumnName("note");
        builder.Property(e => e.RecordedBy).HasColumnName("recorded_by");

        builder.HasIndex(e => new { e.EnvironmentId, e.IncurredOn })
            .HasDatabaseName("ix_operating_expenses_incurred_on");
    }
}
