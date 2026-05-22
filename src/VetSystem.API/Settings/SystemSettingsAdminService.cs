using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
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
        return _mapper.Map<SystemSettingsResponse>(entity);
    }

    public async Task<SystemSettingsResponse> PatchAsync(
        SystemSettingsPatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await LoadAsync(cancellationToken);

        if (request.DefaultExamFee.HasValue) entity.DefaultExamFee = request.DefaultExamFee.Value;
        if (request.EntitlementEnabledGlobal.HasValue) entity.EntitlementEnabledGlobal = request.EntitlementEnabledGlobal.Value;
        if (request.LowStockThresholdPct.HasValue) entity.LowStockThresholdPct = request.LowStockThresholdPct.Value;
        if (request.ExpirationWarningDays.HasValue) entity.ExpirationWarningDays = request.ExpirationWarningDays.Value;
        if (request.TaxEnabled.HasValue) entity.TaxEnabled = request.TaxEnabled.Value;
        if (request.TaxRate.HasValue) entity.TaxRate = request.TaxRate.Value;
        if (request.LogoUrl is not null) entity.LogoUrl = request.LogoUrl;
        if (request.InvoiceTaxDetails is not null) entity.InvoiceTaxDetails = request.InvoiceTaxDetails;
        if (request.Extra is not null) entity.Extra = request.Extra;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<SystemSettingsResponse>(entity);
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
