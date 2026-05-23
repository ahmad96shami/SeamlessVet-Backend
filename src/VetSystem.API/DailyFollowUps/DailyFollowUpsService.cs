using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.DailyFollowUps.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.DailyFollowUps;

/// <summary>
/// Daily follow-up CRUD (PRD §5.2-E, M5 task 11) — per-day entries for a hospitalized case.
/// Clinic-only: creation against a field visit is rejected (field visits don't hospitalize).
/// </summary>
public sealed class DailyFollowUpsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public DailyFollowUpsService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<DailyFollowUpResponse>> ListAsync(
        Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.DailyFollowUps.AsNoTracking();
        if (visitId is { } vid) query = query.Where(f => f.VisitId == vid);

        var rows = await query
            .OrderBy(f => f.EntryDate)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<DailyFollowUpResponse>).ToList();
    }

    public async Task<DailyFollowUpResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.DailyFollowUps.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                  ?? throw new NotFoundException("daily_follow_up", id);
        return _mapper.Map<DailyFollowUpResponse>(row);
    }

    public async Task<DailyFollowUpResponse> CreateAsync(
        DailyFollowUpCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VisitId, cancellationToken)
                    ?? throw new NotFoundException("visit", request.VisitId);

        if (visit.VisitType == VisitType.Field)
        {
            throw new ConflictException("follow_up_clinic_only",
                "Daily follow-ups are for in-clinic hospitalized cases only, not field visits.");
        }

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.DailyFollowUps.IgnoreQueryFilters().AnyAsync(f => f.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("daily_follow_up_id_collision",
                    $"A daily follow-up with id '{id}' already exists.");
            }
        }

        var entry = new DailyFollowUp
        {
            Id = request.Id ?? Guid.Empty,
            VisitId = request.VisitId,
            EntryDate = request.EntryDate,
            Condition = request.Condition,
            Temperature = request.Temperature,
            HeartRate = request.HeartRate,
            RespiratoryRate = request.RespiratoryRate,
            AdministeredMeds = request.AdministeredMeds,
            Notes = request.Notes,
        };

        _db.DailyFollowUps.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<DailyFollowUpResponse>(entry);
    }

    public async Task<DailyFollowUpResponse> UpdateAsync(
        Guid id, DailyFollowUpPatchRequest request, CancellationToken cancellationToken)
    {
        var entry = await _db.DailyFollowUps.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                    ?? throw new NotFoundException("daily_follow_up", id);

        if (request.EntryDate.HasValue) entry.EntryDate = request.EntryDate.Value;
        if (request.Condition is not null) entry.Condition = request.Condition;
        if (request.Temperature.HasValue) entry.Temperature = request.Temperature;
        if (request.HeartRate.HasValue) entry.HeartRate = request.HeartRate;
        if (request.RespiratoryRate.HasValue) entry.RespiratoryRate = request.RespiratoryRate;
        if (request.AdministeredMeds is not null) entry.AdministeredMeds = request.AdministeredMeds;
        if (request.Notes is not null) entry.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<DailyFollowUpResponse>(entry);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await _db.DailyFollowUps.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                    ?? throw new NotFoundException("daily_follow_up", id);

        _db.DailyFollowUps.Remove(entry);
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
