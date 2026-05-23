using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Vaccinations.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Vaccinations;

/// <summary>
/// Vaccination CRUD (PRD §5.2, §6.7, M5 task 12). Targets a single pet or a farm group (customer);
/// <c>NextDueDate</c> is what the M11 reminder job scans. Existence of any referenced pet/customer/
/// visit is validated within the caller's environment via the global query filter.
/// </summary>
public sealed class VaccinationsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public VaccinationsService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<VaccinationResponse>> ListAsync(
        Guid? petId, Guid? customerId, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.Vaccinations.AsNoTracking();
        if (petId is { } pid) query = query.Where(v => v.PetId == pid);
        if (customerId is { } cid) query = query.Where(v => v.CustomerId == cid);
        if (visitId is { } vid) query = query.Where(v => v.VisitId == vid);

        var rows = await query
            .OrderByDescending(v => v.DateGiven)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<VaccinationResponse>).ToList();
    }

    public async Task<VaccinationResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Vaccinations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                  ?? throw new NotFoundException("vaccination", id);
        return _mapper.Map<VaccinationResponse>(row);
    }

    public async Task<VaccinationResponse> CreateAsync(
        VaccinationCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.PetId is { } petId)
        {
            await RequireExistsAsync(_db.Pets.AnyAsync(p => p.Id == petId, cancellationToken), "pet", petId);
        }

        if (request.CustomerId is { } customerId)
        {
            await RequireExistsAsync(
                _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken), "customer", customerId);
        }

        if (request.VisitId is { } visitId)
        {
            await RequireExistsAsync(_db.Visits.AnyAsync(v => v.Id == visitId, cancellationToken), "visit", visitId);
        }

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Vaccinations.IgnoreQueryFilters().AnyAsync(v => v.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("vaccination_id_collision",
                    $"A vaccination with id '{id}' already exists.");
            }
        }

        var vaccination = new Vaccination
        {
            Id = request.Id ?? Guid.Empty,
            PetId = request.PetId,
            CustomerId = request.CustomerId,
            VisitId = request.VisitId,
            VaccineType = request.VaccineType,
            DateGiven = request.DateGiven,
            NextDueDate = request.NextDueDate,
            CertificateUrl = request.CertificateUrl,
        };

        _db.Vaccinations.Add(vaccination);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<VaccinationResponse>(vaccination);
    }

    public async Task<VaccinationResponse> UpdateAsync(
        Guid id, VaccinationPatchRequest request, CancellationToken cancellationToken)
    {
        var vaccination = await _db.Vaccinations.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                          ?? throw new NotFoundException("vaccination", id);

        if (request.VaccineType is not null) vaccination.VaccineType = request.VaccineType;
        if (request.DateGiven.HasValue) vaccination.DateGiven = request.DateGiven.Value;
        if (request.NextDueDate.HasValue) vaccination.NextDueDate = request.NextDueDate;
        if (request.CertificateUrl is not null) vaccination.CertificateUrl = request.CertificateUrl;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<VaccinationResponse>(vaccination);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var vaccination = await _db.Vaccinations.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                          ?? throw new NotFoundException("vaccination", id);

        _db.Vaccinations.Remove(vaccination);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
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
