using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Partnership;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Partnership;

/// <summary>
/// Partner CRUD (PRD §6.8, M10 tasks 2). Admin-only (gated on <c>partnership.manage</c>) and available
/// only in a <c>partnership</c> environment — every operation first asserts the env mode, so a solo
/// environment 404s. Deleting a partner cascades a soft-delete to its shares so no orphaned share rows
/// linger in the profit-distribution math.
/// </summary>
public sealed class PartnersService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public PartnersService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<PartnerResponse>> ListAsync(int? skip, int? take, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var rows = await _db.Partners.AsNoTracking()
            .OrderBy(p => p.DisplayName).ThenBy(p => p.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<PartnerResponse>).ToList();
    }

    public async Task<PartnerResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var partner = await _db.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("partner", id);

        return _mapper.Map<PartnerResponse>(partner);
    }

    public async Task<PartnerResponse> CreateAsync(PartnerCreateRequest request, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        if (request.UserId is { } userId)
        {
            await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == userId, cancellationToken), "user", userId);
        }

        if (request.Id is { } id && id != Guid.Empty
            && await _db.Partners.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new ConflictException("partner_id_collision", $"A partner with id '{id}' already exists.");
        }

        var partner = new Partner
        {
            Id = request.Id ?? Guid.Empty,
            UserId = request.UserId,
            DisplayName = request.DisplayName,
            Notes = request.Notes,
        };

        _db.Partners.Add(partner);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<PartnerResponse>(partner);
    }

    public async Task<PartnerResponse> UpdateAsync(Guid id, PartnerPatchRequest request, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("partner", id);

        if (request.UserId is { } userId)
        {
            await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == userId, cancellationToken), "user", userId);
            partner.UserId = userId;
        }

        if (request.DisplayName is not null) partner.DisplayName = request.DisplayName;
        if (request.Notes is not null) partner.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<PartnerResponse>(partner);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequirePartnershipEnvironmentAsync(cancellationToken);

        var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("partner", id);

        // Cascade the soft-delete to the partner's live shares so they no longer count toward the
        // per-environment ≤ 100% total. The AuditingInterceptor turns each Remove into a soft-delete.
        var shares = await _db.PartnershipShares.Where(s => s.PartnerId == id).ToListAsync(cancellationToken);
        _db.PartnershipShares.RemoveRange(shares);
        _db.Partners.Remove(partner);

        await _db.SaveChangesAsync(cancellationToken);
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
