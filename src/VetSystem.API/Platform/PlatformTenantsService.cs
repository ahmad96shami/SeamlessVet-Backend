using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Identity;
using VetSystem.Application.Platform.Contracts;
using VetSystem.Application.Provisioning;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.API.Platform;

/// <summary>
/// M35 — the platform console's tenant operations (list / get / provision / suspend / reactivate).
/// A platform token carries no <c>environment_id</c>, so the env-scoped query filter would hide every
/// tenant row: every read here uses <c>IgnoreQueryFilters()</c> with an explicit predicate. Writes to
/// <c>environments</c> set <c>UpdatedAt</c> manually because the auditing interceptor only stamps
/// <see cref="Entity"/> types, and call <see cref="IEnvironmentStatusProvider.Invalidate"/> so the
/// live-suspension gate flips within one request rather than after the 45s cache window.
/// </summary>
public sealed class PlatformTenantsService
{
    private readonly ApplicationDbContext _db;
    private readonly IEnvironmentProvisioningService _provisioning;
    private readonly IEnvironmentStatusProvider _statusProvider;
    private readonly IClock _clock;

    public PlatformTenantsService(
        ApplicationDbContext db,
        IEnvironmentProvisioningService provisioning,
        IEnvironmentStatusProvider statusProvider,
        IClock clock)
    {
        _db = db;
        _provisioning = provisioning;
        _statusProvider = statusProvider;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TenantSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var envs = await _db.Environments
            .IgnoreQueryFilters()
            .Where(e => e.DeletedAt == null)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);

        var counts = await UserCountsAsync(cancellationToken);

        return envs
            .Select(e => ToSummary(e, counts.GetValueOrDefault(e.Id)))
            .ToList();
    }

    public async Task<TenantSummary> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var env = await FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("tenant", id);

        var count = await _db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.EnvironmentId == id && u.DeletedAt == null, cancellationToken);

        return ToSummary(env, count);
    }

    public async Task<TenantSummary> ProvisionAsync(
        ProvisionEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _provisioning.ProvisionAsync(request, environmentId: null, cancellationToken);
        return await GetAsync(result.EnvironmentId, cancellationToken);
    }

    public Task<TenantSummary> SuspendAsync(Guid id, CancellationToken cancellationToken)
        => SetStatusAsync(id, EnvironmentStatus.Suspended, cancellationToken);

    public Task<TenantSummary> ReactivateAsync(Guid id, CancellationToken cancellationToken)
        => SetStatusAsync(id, EnvironmentStatus.Active, cancellationToken);

    private async Task<TenantSummary> SetStatusAsync(Guid id, string status, CancellationToken cancellationToken)
    {
        var env = await FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("tenant", id);

        if (env.Status != status)
        {
            env.Status = status;
            env.UpdatedAt = _clock.UtcNow; // interceptor skips non-Entity types — stamp it here.
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Flip the live-suspension gate immediately (don't wait for the cache to expire).
        _statusProvider.Invalidate(id);

        return await GetAsync(id, cancellationToken);
    }

    private Task<DomainEnvironment?> FindAsync(Guid id, CancellationToken cancellationToken)
        => _db.Environments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt == null, cancellationToken);

    private async Task<Dictionary<Guid, int>> UserCountsAsync(CancellationToken cancellationToken)
        => await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.DeletedAt == null)
            .GroupBy(u => u.EnvironmentId)
            .Select(g => new { EnvironmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EnvironmentId, x => x.Count, cancellationToken);

    private static TenantSummary ToSummary(DomainEnvironment e, int userCount)
        => new(e.Id, e.Name, e.Code, e.Mode, e.Status, userCount, e.CreatedAt);
}
