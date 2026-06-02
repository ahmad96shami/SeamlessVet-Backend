namespace VetSystem.Application.Settings.Contracts;

/// <summary>
/// Snapshot of the per-environment <c>system_settings</c> row (PRD §5.7). All admin-tunable
/// values surface here so the Admin UI can change them without a redeploy
/// (CLAUDE.md "Externalized Configuration").
/// </summary>
public sealed record SystemSettingsResponse(
    Guid Id,
    decimal DefaultExamFee,
    decimal DefaultCheckupFee,
    bool EntitlementEnabledGlobal,
    decimal LowStockThresholdPct,
    int ExpirationWarningDays,
    bool TaxEnabled,
    decimal TaxRate,
    string? LogoUrl,
    string? InvoiceTaxDetails,
    string? Extra,
    // M17 — night-stay tunables, surfaced from the `extra` bag for the Admin UI.
    decimal NightStayRateMedical,
    decimal NightStayRateIcu,
    decimal NightStayRateHotel,
    int NightStayCheckoutHour,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Partial update payload — every field is optional so an admin can change a single value
/// (e.g. just <c>DefaultExamFee</c>) without round-tripping the whole row. The night-stay fields
/// are merged into the <c>extra</c> JSONB bag (other keys preserved); <c>Extra</c> stays available
/// as the raw escape hatch.
/// </summary>
public sealed record SystemSettingsPatchRequest(
    decimal? DefaultExamFee,
    decimal? DefaultCheckupFee,
    bool? EntitlementEnabledGlobal,
    decimal? LowStockThresholdPct,
    int? ExpirationWarningDays,
    bool? TaxEnabled,
    decimal? TaxRate,
    string? LogoUrl,
    string? InvoiceTaxDetails,
    string? Extra,
    decimal? NightStayRateMedical,
    decimal? NightStayRateIcu,
    decimal? NightStayRateHotel,
    int? NightStayCheckoutHour);
