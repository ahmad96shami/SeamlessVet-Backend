using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Pets.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Pets;

/// <summary>
/// Builds a pet's medical timeline (PRD §5.2, M5 task 17) — the pet's visits (clinic + field)
/// newest-first, each enriched with its procedures, prescriptions, and visit-linked vaccinations.
/// Children are loaded in three batched queries (one per child table) and grouped in memory to
/// avoid an N+1 per visit. Read-only.
/// </summary>
public sealed class PetTimelineService
{
    private readonly ApplicationDbContext _db;

    public PetTimelineService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PetTimelineResponse> GetAsync(
        Guid petId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? doctorId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Pets.AnyAsync(p => p.Id == petId, cancellationToken))
        {
            throw new NotFoundException("pet", petId);
        }

        var visitsQuery = _db.Visits.AsNoTracking().Where(v => v.PetId == petId);
        if (from is { } f) visitsQuery = visitsQuery.Where(v => v.StartedAt >= f);
        if (to is { } t) visitsQuery = visitsQuery.Where(v => v.StartedAt <= t);
        if (doctorId is { } d) visitsQuery = visitsQuery.Where(v => v.DoctorId == d);

        var visits = await visitsQuery
            .OrderByDescending(v => v.StartedAt)
            .ToListAsync(cancellationToken);

        if (visits.Count == 0)
        {
            return new PetTimelineResponse(petId, []);
        }

        var visitIds = visits.Select(v => v.Id).ToList();

        var procedures = (await _db.Procedures.AsNoTracking()
                .Where(p => visitIds.Contains(p.VisitId))
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.VisitId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var prescriptions = (await _db.Prescriptions.AsNoTracking()
                .Where(p => visitIds.Contains(p.VisitId))
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.VisitId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var vaccinations = (await _db.Vaccinations.AsNoTracking()
                .Where(v => v.VisitId != null && visitIds.Contains(v.VisitId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.VisitId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var entries = visits.Select(v => new PetTimelineVisit(
            v.Id,
            v.VisitType,
            v.VisitNumber,
            v.Status,
            v.StartedAt,
            v.EndedAt,
            v.DoctorId,
            v.PreliminaryDiagnosis,
            v.FinalDiagnosis,
            procedures.GetValueOrDefault(v.Id, [])
                .Select(p => new TimelineProcedure(p.Id, p.ServiceId, p.ResultText, p.Price)).ToList(),
            prescriptions.GetValueOrDefault(v.Id, [])
                .Select(p => new TimelinePrescription(p.Id, p.ProductId, p.DispenseType, p.Quantity, p.Dosage)).ToList(),
            vaccinations.GetValueOrDefault(v.Id, [])
                .Select(x => new TimelineVaccination(x.Id, x.VaccineType, x.DateGiven, x.NextDueDate)).ToList()))
            .ToList();

        return new PetTimelineResponse(petId, entries);
    }
}
