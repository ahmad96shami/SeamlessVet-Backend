using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Farms.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Farms;

/// <summary>
/// Farm CRUD (M15). Farms are attached to a customer exactly the way pets are; they carry no
/// assigned doctor of their own (they inherit the owning customer's), so a farm streams to the
/// field device through the existing <c>by_customer</c> scope. Ledger ownership stays single-ledger
/// (the customer's) this milestone — per-farm ledgers land in M16.
/// </summary>
public sealed class FarmsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public FarmsService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<FarmResponse>> ListAsync(
        Guid? customerId,
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var query = _db.Farms.AsNoTracking();

        if (customerId is { } cid)
        {
            query = query.Where(f => f.CustomerId == cid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(f =>
                EF.Functions.ILike(f.Name, pattern) ||
                (f.Location != null && EF.Functions.ILike(f.Location, pattern)));
        }

        var rows = await query
            .OrderBy(f => f.Name)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<FarmResponse>).ToList();
    }

    public async Task<FarmResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var farm = await _db.Farms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new NotFoundException("farm", id);

        return _mapper.Map<FarmResponse>(farm);
    }

    public async Task<FarmResponse> CreateAsync(FarmRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireCustomerExistsAsync(request.CustomerId, cancellationToken);

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Farms
                .IgnoreQueryFilters()
                .AnyAsync(f => f.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("farm_id_collision", $"A farm with id '{id}' already exists.");
            }
        }

        var entity = new Farm
        {
            Id = request.Id ?? Guid.Empty,
            CustomerId = request.CustomerId,
            Name = request.Name,
            Kind = request.Kind,
            Location = request.Location,
            AnimalType = request.AnimalType,
            HeadCount = request.HeadCount,
            Notes = request.Notes,
        };

        _db.Farms.Add(entity);

        // M16: a farm owns its ledger, created in the same transaction (mirrors the customer-ledger
        // seed in CustomersService.CreateAsync). AuditingInterceptor stamps id + environment_id +
        // timestamps, so we leave those at default.
        _db.Ledgers.Add(new Ledger
        {
            FarmId = entity.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
        });

        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<FarmResponse>(entity);
    }

    public async Task<FarmResponse> UpdateAsync(
        Guid id,
        FarmPatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Farms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                     ?? throw new NotFoundException("farm", id);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.Kind is not null) entity.Kind = request.Kind;
        if (request.Location is not null) entity.Location = request.Location;
        if (request.AnimalType is not null) entity.AnimalType = request.AnimalType;
        if (request.HeadCount.HasValue) entity.HeadCount = request.HeadCount;
        if (request.Notes is not null) entity.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<FarmResponse>(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Farms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                     ?? throw new NotFoundException("farm", id);

        _db.Farms.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private async Task RequireCustomerExistsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var exists = await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("customer", customerId);
        }
    }
}
