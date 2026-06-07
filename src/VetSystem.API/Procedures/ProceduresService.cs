using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Application.Procedures.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Procedures;

/// <summary>
/// Procedure CRUD (PRD §5.2-C, M5 task 7). Each procedure belongs to a visit and links to a catalog
/// <see cref="Service"/>; its <c>Price</c> is snapshotted at create time (defaulting to the service's
/// <c>DefaultPrice</c> when not supplied) so later catalog edits don't rewrite history.
/// </summary>
public sealed class ProceduresService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public ProceduresService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ProcedureResponse>> ListAsync(
        Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.Procedures.AsNoTracking();
        if (visitId is { } vid) query = query.Where(p => p.VisitId == vid);

        var rows = await query
            .OrderBy(p => p.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ProcedureResponse>).ToList();
    }

    public async Task<ProcedureResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var procedure = await _db.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                        ?? throw new NotFoundException("procedure", id);
        return _mapper.Map<ProcedureResponse>(procedure);
    }

    public async Task<ProcedureResponse> CreateAsync(ProcedureCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireVisitAsync(request.VisitId, cancellationToken);

        var price = request.Price ?? await ResolveDefaultPriceAsync(request.ServiceId, cancellationToken);

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Procedures.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("procedure_id_collision", $"A procedure with id '{id}' already exists.");
            }
        }

        var procedure = new Procedure
        {
            Id = request.Id ?? Guid.Empty,
            VisitId = request.VisitId,
            ServiceId = request.ServiceId,
            ResultText = request.ResultText,
            ResultFileUrl = request.ResultFileUrl,
            Price = price,
        };

        _db.Procedures.Add(procedure);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ProcedureResponse>(procedure);
    }

    public async Task<ProcedureResponse> UpdateAsync(
        Guid id, ProcedurePatchRequest request, CancellationToken cancellationToken)
    {
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                        ?? throw new NotFoundException("procedure", id);

        // Once billed, the money/identity fields are frozen (BilledChargeGuard) — the issued
        // invoice line snapshots them. Change-detected (not presence-detected) so a form that
        // round-trips the unchanged price can still edit the clinical result text.
        var changesService = request.ServiceId is { } sid && sid != procedure.ServiceId;
        var changesPrice = request.Price.HasValue && request.Price.Value != procedure.Price;
        if (changesService || changesPrice)
        {
            await BilledChargeGuard.EnsureProcedureNotBilledAsync(_db, id, cancellationToken);
        }

        if (request.ServiceId is { } serviceId)
        {
            await RequireServiceAsync(serviceId, cancellationToken);
            procedure.ServiceId = serviceId;
        }

        if (request.ResultText is not null) procedure.ResultText = request.ResultText;
        if (request.ResultFileUrl is not null) procedure.ResultFileUrl = request.ResultFileUrl;
        if (request.Price.HasValue) procedure.Price = request.Price.Value;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ProcedureResponse>(procedure);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                        ?? throw new NotFoundException("procedure", id);

        await BilledChargeGuard.EnsureProcedureNotBilledAsync(_db, id, cancellationToken);

        _db.Procedures.Remove(procedure);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<decimal> ResolveDefaultPriceAsync(Guid? serviceId, CancellationToken cancellationToken)
    {
        if (serviceId is not { } sid)
        {
            return 0m;
        }

        var service = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, cancellationToken)
                      ?? throw new NotFoundException("service", sid);
        return service.DefaultPrice;
    }

    private async Task RequireServiceAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        if (!await _db.Services.AnyAsync(s => s.Id == serviceId, cancellationToken))
        {
            throw new NotFoundException("service", serviceId);
        }
    }

    private async Task RequireVisitAsync(Guid visitId, CancellationToken cancellationToken)
    {
        if (!await _db.Visits.AnyAsync(v => v.Id == visitId, cancellationToken))
        {
            throw new NotFoundException("visit", visitId);
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
