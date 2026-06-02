using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.ToTable("system_settings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.DefaultExamFee).HasColumnName("default_exam_fee").HasColumnType("numeric(14,2)");
        builder.Property(s => s.DefaultCheckupFee).HasColumnName("default_checkup_fee").HasColumnType("numeric(14,2)");
        builder.Property(s => s.EntitlementEnabledGlobal).HasColumnName("entitlement_enabled_global");
        builder.Property(s => s.LowStockThresholdPct).HasColumnName("low_stock_threshold_pct").HasColumnType("numeric(5,2)");
        builder.Property(s => s.ExpirationWarningDays).HasColumnName("expiration_warning_days");
        builder.Property(s => s.TaxEnabled).HasColumnName("tax_enabled");
        builder.Property(s => s.TaxRate).HasColumnName("tax_rate").HasColumnType("numeric(5,2)");
        builder.Property(s => s.LogoUrl).HasColumnName("logo_url");
        builder.Property(s => s.InvoiceTaxDetails).HasColumnName("invoice_tax_details").HasColumnType("jsonb");
        builder.Property(s => s.Extra).HasColumnName("extra").HasColumnType("jsonb");

        // SCHEMA §9: singleton per environment.
        builder.HasIndex(s => s.EnvironmentId)
            .HasDatabaseName("ux_system_settings_env")
            .IsUnique();
    }
}
