using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Catalog.Contracts;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Catalog;

/// <summary>Admin operations on the services catalog. Mirrors <see cref="ProductsAdminService"/>.</summary>
public sealed class ServicesAdminService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public ServicesAdminService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ServiceResponse>> ListAsync(
        string? search,
        string? category,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var query = _db.Services.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.NameAr, pattern) ||
                (s.NameLatin != null && EF.Functions.ILike(s.NameLatin, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(s => s.Category == category);
        }

        var rows = await query
            .OrderBy(s => s.NameAr)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ServiceResponse>).ToList();
    }

    public async Task<ServiceResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                     ?? throw new NotFoundException("service", id);
        return _mapper.Map<ServiceResponse>(entity);
    }

    public async Task<ServiceResponse> CreateAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Services
                .IgnoreQueryFilters()
                .AnyAsync(s => s.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("service_id_collision", $"A service with id '{id}' already exists.");
            }
        }

        var entity = new Service
        {
            Id = request.Id ?? Guid.Empty,
            NameAr = request.NameAr,
            NameLatin = request.NameLatin,
            Category = request.Category,
            DefaultPrice = request.DefaultPrice,
        };

        _db.Services.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ServiceResponse>(entity);
    }

    public async Task<ServiceResponse> UpdateAsync(
        Guid id,
        ServicePatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                     ?? throw new NotFoundException("service", id);

        if (request.NameAr is not null) entity.NameAr = request.NameAr;
        if (request.NameLatin is not null) entity.NameLatin = request.NameLatin;
        if (request.Category is not null) entity.Category = request.Category;
        if (request.DefaultPrice.HasValue) entity.DefaultPrice = request.DefaultPrice.Value;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ServiceResponse>(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                     ?? throw new NotFoundException("service", id);

        _db.Services.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
