namespace VetSystem.Application.Settings.Contracts;

/// <summary>
/// Snapshot of the per-environment <c>system_settings</c> row (PRD §5.7). All admin-tunable
/// values surface here so the Admin UI can change them without a redeploy
/// (CLAUDE.md "Externalized Configuration").
/// </summary>
public sealed record SystemSettingsResponse(
    Guid Id,
    decimal DefaultExamFee,
    bool EntitlementEnabledGlobal,
    decimal LowStockThresholdPct,
    int ExpirationWarningDays,
    bool TaxEnabled,
    decimal TaxRate,
    string? LogoUrl,
    string? InvoiceTaxDetails,
    string? Extra,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Partial update payload — every field is optional so an admin can change a single value
/// (e.g. just <c>DefaultExamFee</c>) without round-tripping the whole row.
/// </summary>
public sealed record SystemSettingsPatchRequest(
    decimal? DefaultExamFee,
    bool? EntitlementEnabledGlobal,
    decimal? LowStockThresholdPct,
    int? ExpirationWarningDays,
    bool? TaxEnabled,
    decimal? TaxRate,
    string? LogoUrl,
    string? InvoiceTaxDetails,
    string? Extra);
