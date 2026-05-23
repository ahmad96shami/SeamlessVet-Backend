using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Partnership;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Partnership;

/// <summary>
/// Partnership-share CRUD (PRD §6.8, M10 task 3). Admin-only + partnership-environment-only (same gate
/// as <see cref="PartnersService"/>). Every create/edit re-checks the per-environment invariant via
/// <see cref="IPartnershipValidator"/>: the active shares — across all partners — may not exceed 100%
/// on any date once this row's window is folded in.
/// </summary>
public sealed class PartnershipSharesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IPartnershipValidator _validator;
    private readonly IMapper _mapper;

    public PartnershipSharesService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IPartnershipValidator validator,
        IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _validator = validator;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<PartnershipShareResponse>> ListAsync(
        Guid? partnerId, DateOnly? activeOn, int? skip, int? take, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var query = _db.PartnershipShares.AsNoTracking();
        if (partnerId is { } pid) query = query.Where(s => s.PartnerId == pid);
        if (activeOn is { } asOf)
        {
            query = query.Where(s => s.EffectiveFrom <= asOf && (s.EffectiveTo == null || s.EffectiveTo >= asOf));
        }

        var rows = await query
            .OrderByDescending(s => s.EffectiveFrom).ThenBy(s => s.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<PartnershipShareResponse>).ToList();
    }

    public async Task<PartnershipShareResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var share = await _db.PartnershipShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                    ?? throw new NotFoundException("partnership_share", id);

        return _mapper.Map<PartnershipShareResponse>(share);
    }

    public async Task<PartnershipShareResponse> CreateAsync(
        PartnershipShareCreateRequest request, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        await RequireExistsAsync(
            _db.Partners.AnyAsync(p => p.Id == request.PartnerId, cancellationToken), "partner", request.PartnerId);

        if (request.Id is { } id && id != Guid.Empty
            && await _db.PartnershipShares.IgnoreQueryFilters().AnyAsync(s => s.Id == id, cancellationToken))
        {
            throw new ConflictException("partnership_share_id_collision", $"A partnership share with id '{id}' already exists.");
        }

        var candidate = new ShareWindow(request.EffectiveFrom, request.EffectiveTo, request.SharePercent);
        await EnsureWithinLimitAsync(excludeShareId: null, candidate, cancellationToken);

        var share = new PartnershipShare
        {
            Id = request.Id ?? Guid.Empty,
            PartnerId = request.PartnerId,
            SharePercent = request.SharePercent,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };

        _db.PartnershipShares.Add(share);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<PartnershipShareResponse>(share);
    }

    public async Task<PartnershipShareResponse> UpdateAsync(
        Guid id, PartnershipSharePatchRequest request, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var share = await _db.PartnershipShares.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                    ?? throw new NotFoundException("partnership_share", id);

        var newPercent = request.SharePercent ?? share.SharePercent;
        var newFrom = request.EffectiveFrom ?? share.EffectiveFrom;
        var newTo = request.EffectiveTo ?? share.EffectiveTo;

        var candidate = new ShareWindow(newFrom, newTo, newPercent);
        await EnsureWithinLimitAsync(excludeShareId: id, candidate, cancellationToken);

        share.SharePercent = newPercent;
        share.EffectiveFrom = newFrom;
        share.EffectiveTo = newTo;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<PartnershipShareResponse>(share);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var share = await _db.PartnershipShares.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                    ?? throw new NotFoundException("partnership_share", id);

        _db.PartnershipShares.Remove(share);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Folds the candidate window into the environment's other live shares (the global filter scopes to
    /// the current env and excludes soft-deleted rows) and asserts the ≤ 100% invariant on every date.
    /// </summary>
    private async Task EnsureWithinLimitAsync(Guid? excludeShareId, ShareWindow candidate, CancellationToken cancellationToken)
    {
        var others = await _db.PartnershipShares.AsNoTracking()
            .Where(s => excludeShareId == null || s.Id != excludeShareId)
            .Select(s => new ShareWindow(s.EffectiveFrom, s.EffectiveTo, s.SharePercent))
            .ToListAsync(cancellationToken);

        others.Add(candidate);
        _validator.EnsureWithinLimit(others);
    }

    private async Task<Guid> RequirePartnershipEnvironmentAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } environmentId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        await PartnershipGuard.EnsurePartnershipEnvironmentAsync(_db, environmentId, cancellationToken);
        return environmentId;
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
