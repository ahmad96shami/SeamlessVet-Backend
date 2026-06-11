using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.DoctorPartners.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.DoctorPartners;

/// <summary>
/// M30 doctor-partner CRUD (SCHEMA §4). Online-only center-web (admin/accountant). A doctor-partner
/// carries a mandatory user link and is the payee for the supervision fees they earn (the AP mirror of
/// <see cref="Supplier"/>, distinct from the M10 investor <see cref="Partner"/>). <see cref="CreateAsync"/>
/// validates the user, enforces one partner per user, and seeds the matching
/// <see cref="DoctorPartnerLedger"/> in the same transaction so the entitlement-credit and payment flows
/// always have one to post into.
/// </summary>
public sealed class DoctorPartnersService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public DoctorPartnersService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<DoctorPartnerResponse>> ListAsync(
        string? search,
        string? ledgerStatus,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (ledgerStatus is not null && !LedgerStatus.All.Contains(ledgerStatus))
        {
            throw new ConflictException("invalid_ledger_status", $"ledgerStatus '{ledgerStatus}' is not valid.");
        }

        // The display name lives on the linked user; project (partner, name) so search/order can use it.
        var joined = _db.DoctorPartners.AsNoTracking()
            .Join(_db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { Partner = p, u.FullName });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            joined = joined.Where(x => EF.Functions.ILike(x.FullName, pattern));
        }

        if (ledgerStatus is { } status)
        {
            joined = joined.Where(x =>
                _db.DoctorPartnerLedgers.Any(l => l.DoctorPartnerId == x.Partner.Id && l.Status == status));
        }

        var page = await joined
            .OrderBy(x => x.FullName)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return [];
        }

        var ids = page.Select(x => x.Partner.Id).ToList();
        var ledgers = await _db.DoctorPartnerLedgers.AsNoTracking()
            .Where(l => ids.Contains(l.DoctorPartnerId))
            .ToDictionaryAsync(l => l.DoctorPartnerId, l => new { l.Balance, l.Status }, cancellationToken);

        return page.Select(x =>
        {
            ledgers.TryGetValue(x.Partner.Id, out var ledger);
            return ToResponse(x.Partner, x.FullName, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
        }).ToList();
    }

    public async Task<DoctorPartnerResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var partner = await _db.DoctorPartners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("doctor_partner", id);

        var doctorName = await ResolveNameAsync(partner.UserId, cancellationToken);
        var ledger = await _db.DoctorPartnerLedgers.AsNoTracking()
            .Where(l => l.DoctorPartnerId == id)
            .Select(l => new { l.Balance, l.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return ToResponse(partner, doctorName, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    public async Task<DoctorPartnerResponse> CreateAsync(DoctorPartnerRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.Id is { } id && id != Guid.Empty
            && await _db.DoctorPartners.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new ConflictException("doctor_partner_id_collision", $"A doctor-partner with id '{id}' already exists.");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken))
        {
            throw new NotFoundException("user", request.UserId);
        }

        if (await _db.DoctorPartners.AnyAsync(p => p.UserId == request.UserId, cancellationToken))
        {
            throw new ConflictException("doctor_partner_user_taken",
                "This user already has a doctor-partner record.");
        }

        var entity = new DoctorPartner
        {
            Id = request.Id ?? Guid.Empty,
            UserId = request.UserId,
            Notes = request.Notes,
        };

        _db.DoctorPartners.Add(entity);

        // One ledger per partner is created in the same transaction (the AuditingInterceptor stamps
        // id + environment_id + timestamps), so the entitlement-credit and payment flows always have one.
        var ledger = new DoctorPartnerLedger
        {
            DoctorPartnerId = entity.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
        };
        _db.DoctorPartnerLedgers.Add(ledger);

        await _db.SaveChangesAsync(cancellationToken);

        var doctorName = await ResolveNameAsync(entity.UserId, cancellationToken);
        return ToResponse(entity, doctorName, ledger.Balance, ledger.Status);
    }

    public async Task<DoctorPartnerResponse> UpdateAsync(
        Guid id, DoctorPartnerPatchRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.DoctorPartners.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("doctor_partner", id);

        if (request.Notes is not null) entity.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);

        var doctorName = await ResolveNameAsync(entity.UserId, cancellationToken);
        var ledger = await _db.DoctorPartnerLedgers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.DoctorPartnerId == entity.Id, cancellationToken);
        return ToResponse(entity, doctorName, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.DoctorPartners.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("doctor_partner", id);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.DoctorPartners.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ResolveNameAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    private DoctorPartnerResponse ToResponse(DoctorPartner partner, string doctorName, decimal balance, string ledgerStatus) =>
        _mapper.Map<DoctorPartnerResponse>(partner) with
        {
            DoctorName = doctorName,
            Balance = balance,
            LedgerStatus = ledgerStatus,
        };

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
