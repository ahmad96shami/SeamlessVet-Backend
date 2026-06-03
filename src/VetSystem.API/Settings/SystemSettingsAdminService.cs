using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Settings;
using VetSystem.Application.Settings.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Settings;

/// <summary>
/// Admin operations on the singleton per-environment <c>system_settings</c> row (PRD §5.7).
/// The row is seeded with defaults at bootstrap; this service exposes the read and partial-update
/// path that the Admin UI uses. SCHEMA "Key invariants" #1/#4 reads from these values at runtime.
/// </summary>
public sealed class SystemSettingsAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public SystemSettingsAdminService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<SystemSettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var entity = await LoadAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<SystemSettingsResponse> PatchAsync(
        SystemSettingsPatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await LoadAsync(cancellationToken);

        if (request.DefaultExamFee.HasValue) entity.DefaultExamFee = request.DefaultExamFee.Value;
        if (request.DefaultCheckupFee.HasValue) entity.DefaultCheckupFee = request.DefaultCheckupFee.Value;
        if (request.EntitlementEnabledGlobal.HasValue) entity.EntitlementEnabledGlobal = request.EntitlementEnabledGlobal.Value;
        if (request.LowStockThresholdPct.HasValue) entity.LowStockThresholdPct = request.LowStockThresholdPct.Value;
        if (request.ExpirationWarningDays.HasValue) entity.ExpirationWarningDays = request.ExpirationWarningDays.Value;
        if (request.TaxEnabled.HasValue) entity.TaxEnabled = request.TaxEnabled.Value;
        if (request.TaxRate.HasValue) entity.TaxRate = request.TaxRate.Value;
        if (request.LogoUrl is not null) entity.LogoUrl = request.LogoUrl;
        if (request.InvoiceTaxDetails is not null) entity.InvoiceTaxDetails = request.InvoiceTaxDetails;
        if (request.Extra is not null) entity.Extra = request.Extra;

        // M17 — night-stay tunables merge into the `extra` bag (applied after a raw Extra set so
        // explicit fields win; other extra keys are preserved by WriteInto).
        if (request.NightStayRateMedical.HasValue || request.NightStayRateIcu.HasValue
            || request.NightStayRateHotel.HasValue || request.NightStayCheckoutHour.HasValue)
        {
            var current = NightStaySettings.FromExtra(entity.Extra);
            var updated = current with
            {
                RateMedical = request.NightStayRateMedical ?? current.RateMedical,
                RateIcu = request.NightStayRateIcu ?? current.RateIcu,
                RateHotel = request.NightStayRateHotel ?? current.RateHotel,
                CheckoutHour = request.NightStayCheckoutHour ?? current.CheckoutHour,
            };
            entity.Extra = updated.WriteInto(entity.Extra);
        }

        // M18 — default medication-reminder lead-time merges into the same `extra` bag (other keys preserved).
        if (request.MedicationReminderLeadMinutes.HasValue)
        {
            var current = MedicationReminderSettings.FromExtra(entity.Extra);
            var updated = current with { DefaultLeadMinutes = request.MedicationReminderLeadMinutes.Value };
            entity.Extra = updated.WriteInto(entity.Extra);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    /// <summary>Maps the entity and folds the night-stay + medication-reminder tunables out of the <c>extra</c> bag.</summary>
    private SystemSettingsResponse ToResponse(SystemSettings entity)
    {
        var ns = NightStaySettings.FromExtra(entity.Extra);
        var mr = MedicationReminderSettings.FromExtra(entity.Extra);
        return _mapper.Map<SystemSettingsResponse>(entity) with
        {
            NightStayRateMedical = ns.RateMedical,
            NightStayRateIcu = ns.RateIcu,
            NightStayRateHotel = ns.RateHotel,
            NightStayCheckoutHour = ns.CheckoutHour,
            MedicationReminderLeadMinutes = mr.DefaultLeadMinutes,
        };
    }

    private async Task<SystemSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return await _db.SystemSettings.FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("system_settings", envId);
    }
}
