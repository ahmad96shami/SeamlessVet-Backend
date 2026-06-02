using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §9 — per-environment configuration (PRD §5.7). Singleton: UNIQUE on
/// <c>environment_id</c> enforced by index. Every admin-tunable business value lives here
/// so the Admin UI can change it without a redeploy (CLAUDE.md "Externalized Configuration").
/// </summary>
public sealed class SystemSettings : Entity
{
    public decimal DefaultExamFee { get; set; }

    /// <summary>
    /// Default in-clinic checkup fee (رسوم الكشف, M17 / PRD §18.7) auto-applied to new in-clinic
    /// visits. Night-stay per-night rates + the daily checkout hour live in <see cref="Extra"/>
    /// (see <c>NightStaySettings</c>).
    /// </summary>
    public decimal DefaultCheckupFee { get; set; }

    public bool EntitlementEnabledGlobal { get; set; } = true;

    public decimal LowStockThresholdPct { get; set; }

    public int ExpirationWarningDays { get; set; } = 30;

    public bool TaxEnabled { get; set; }

    public decimal TaxRate { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>JSONB bag for tax/regulatory invoice fields (CR number, VAT IDs, …).</summary>
    public string? InvoiceTaxDetails { get; set; }

    /// <summary>JSONB bag for extensibility — additional admin-tunable values without a migration.</summary>
    public string? Extra { get; set; }
}
