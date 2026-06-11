using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.FullName).HasColumnName("full_name").IsRequired().HasMaxLength(200);
        builder.Property(e => e.JobTitle).HasColumnName("job_title").HasMaxLength(120);
        builder.Property(e => e.MonthlySalary).HasColumnName("monthly_salary").HasColumnType("numeric(14,2)");
        builder.Property(e => e.Active).HasColumnName("active").IsRequired();
        builder.Property(e => e.HiredAt).HasColumnName("hired_at");
        builder.Property(e => e.Notes).HasColumnName("notes");

        // One live employee per staff account (when linked). Filtered on the soft-delete flag so a
        // deleted employee can be re-created for the same user; the partial index also allows many
        // non-user employees (NULL user_id is exempt from the uniqueness check in Postgres).
        builder.HasIndex(e => new { e.EnvironmentId, e.UserId })
            .HasDatabaseName("ux_employees_user")
            .IsUnique()
            .HasFilter("user_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
