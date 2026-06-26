using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Pets.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Pets;

/// <summary>
/// Pet CRUD (PRD §5.1, M3). Pets always belong to a customer; ownership transfers go through
/// <see cref="TransferAsync"/> rather than a free-form PATCH so the rule is unambiguous and
/// can be audited as a single action.
/// </summary>
public sealed class PetsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public PetsService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<PetResponse>> ListAsync(
        Guid? customerId,
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var query = _db.Pets.AsNoTracking();

        if (customerId is { } cid)
        {
            query = query.Where(p => p.CustomerId == cid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                (p.MicrochipNo != null && EF.Functions.ILike(p.MicrochipNo, pattern)));
        }

        var rows = await query
            .OrderBy(p => p.Name)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<PetResponse>).ToList();
    }

    public async Task<PetResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var pet = await _db.Pets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("pet", id);

        return _mapper.Map<PetResponse>(pet);
    }

    public async Task<PetResponse> CreateAsync(PetRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireCustomerExistsAsync(request.CustomerId, cancellationToken);

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Pets
                .IgnoreQueryFilters()
                .AnyAsync(p => p.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("pet_id_collision", $"A pet with id '{id}' already exists.");
            }
        }

        var entity = new Pet
        {
            Id = request.Id ?? Guid.Empty,
            CustomerId = request.CustomerId,
            Name = request.Name,
            Species = request.Species,
            Breed = request.Breed,
            Sex = request.Sex,
            DateOfBirth = request.DateOfBirth,
            ColorMarks = request.ColorMarks,
            WeightLatest = request.WeightLatest,
            PhotoUrl = request.PhotoUrl,
            MicrochipNo = request.MicrochipNo,
            HealthNotes = request.HealthNotes,
            IsNeutered = request.IsNeutered,
        };

        _db.Pets.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<PetResponse>(entity);
    }

    public async Task<PetResponse> UpdateAsync(
        Guid id,
        PetPatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Pets.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("pet", id);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.Species is not null) entity.Species = request.Species;
        if (request.Breed is not null) entity.Breed = request.Breed;
        if (request.Sex is not null) entity.Sex = request.Sex;
        if (request.DateOfBirth.HasValue) entity.DateOfBirth = request.DateOfBirth;
        if (request.ColorMarks is not null) entity.ColorMarks = request.ColorMarks;
        if (request.WeightLatest.HasValue) entity.WeightLatest = request.WeightLatest;
        if (request.PhotoUrl is not null) entity.PhotoUrl = request.PhotoUrl;
        if (request.MicrochipNo is not null) entity.MicrochipNo = request.MicrochipNo;
        if (request.HealthNotes is not null) entity.HealthNotes = request.HealthNotes;
        if (request.IsNeutered.HasValue) entity.IsNeutered = request.IsNeutered;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<PetResponse>(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Pets.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("pet", id);

        _db.Pets.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PetResponse> TransferAsync(
        Guid id,
        PetTransferRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Pets.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("pet", id);

        if (request.TargetCustomerId == entity.CustomerId)
        {
            throw new ConflictException("pet_transfer_same_owner",
                "Target customer is the current owner; no transfer to apply.");
        }

        await RequireCustomerExistsAsync(request.TargetCustomerId, cancellationToken);

        entity.CustomerId = request.TargetCustomerId;
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<PetResponse>(entity);
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
