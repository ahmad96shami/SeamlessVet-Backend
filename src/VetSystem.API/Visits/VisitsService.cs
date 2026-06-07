using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Application.Visits;
using VetSystem.Application.Visits.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Visits;

/// <summary>
/// Visit lifecycle (PRD §5.2, M5 tasks 4–6). Serves both in-clinic and field encounters through
/// one model. The status state machine (<see cref="VisitStatus.CanTransition"/>) is enforced here:
/// section edits are allowed only while the visit is non-terminal, and the terminal transitions go
/// through <see cref="CompleteAsync"/> / <see cref="CancelAsync"/>, which stamp <c>ended_at</c> and
/// make the row server-authoritative (PRD §8.4).
/// </summary>
public sealed class VisitsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IVisitNumberValidator _visitNumbers;
    private readonly IVisitNumberGenerator _visitNumberGen;
    private readonly ILedgerService _ledgers;
    private readonly IOwnerLedgerResolver _ownerLedger;

    public VisitsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IVisitNumberValidator visitNumbers,
        IVisitNumberGenerator visitNumberGen,
        ILedgerService ledgers,
        IOwnerLedgerResolver ownerLedger)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _visitNumbers = visitNumbers;
        _visitNumberGen = visitNumberGen;
        _ledgers = ledgers;
        _ownerLedger = ownerLedger;
    }

    public async Task<IReadOnlyList<VisitResponse>> ListAsync(
        Guid? customerId,
        Guid? petId,
        Guid? doctorId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var query = _db.Visits.AsNoTracking();

        if (customerId is { } cid) query = query.Where(v => v.CustomerId == cid);
        if (petId is { } pid) query = query.Where(v => v.PetId == pid);
        if (doctorId is { } did) query = query.Where(v => v.DoctorId == did);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(v => v.Status == status);

        var rows = await query
            .OrderByDescending(v => v.StartedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<VisitResponse>).ToList();
    }

    public async Task<VisitResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                    ?? throw new NotFoundException("visit", id);

        return _mapper.Map<VisitResponse>(visit);
    }

    public async Task<VisitResponse> CreateAsync(VisitCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        await RequireExistsAsync(_db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken),
            "customer", request.CustomerId);
        await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == request.DoctorId, cancellationToken),
            "doctor", request.DoctorId);

        if (request.ReceptionistId is { } receptionistId)
        {
            await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == receptionistId, cancellationToken),
                "receptionist", receptionistId);
        }

        if (request.PetId is { } petId)
        {
            var pet = await _db.Pets.FirstOrDefaultAsync(p => p.Id == petId, cancellationToken)
                      ?? throw new NotFoundException("pet", petId);
            if (pet.CustomerId != request.CustomerId)
            {
                throw new ConflictException("pet_customer_mismatch",
                    "The pet does not belong to the visit's customer.");
            }
        }

        // M15 — attribute the visit to a farm of the same customer (no ledger change this milestone).
        if (request.FarmId is { } farmId)
        {
            await RequireFarmBelongsToCustomerAsync(farmId, request.CustomerId, cancellationToken);
        }

        var visitNumber = string.IsNullOrWhiteSpace(request.VisitNumber) ? null : request.VisitNumber.Trim();
        if (visitNumber is not null)
        {
            await _visitNumbers.ValidateAsync(visitNumber, excludeVisitId: null, cancellationToken);
        }
        else
        {
            // Web clients send null — auto-assign {prefix}-{n} when the creator has a NumberPrefix.
            visitNumber = await _visitNumberGen.NextForCurrentUserAsync(cancellationToken);
        }

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Visits.IgnoreQueryFilters().AnyAsync(v => v.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("visit_id_collision", $"A visit with id '{id}' already exists.");
            }
        }

        if (request.FollowUpOfVisitId is { } originVisitId)
        {
            await RequireExistsAsync(_db.Visits.AnyAsync(v => v.Id == originVisitId, cancellationToken),
                "visit", originVisitId);
        }

        var checkupFee = await ResolveCheckupFeeAsync(request.VisitType, request.CheckupFeeApplied, cancellationToken);

        var visit = new Visit
        {
            Id = request.Id ?? Guid.Empty,
            VisitType = request.VisitType,
            VisitNumber = visitNumber,
            CustomerId = request.CustomerId,
            FarmId = request.FarmId,
            PetId = request.PetId,
            DoctorId = request.DoctorId,
            ReceptionistId = request.ReceptionistId,
            Status = request.Status ?? VisitStatus.Open,
            StartedAt = request.StartedAt ?? _clock.UtcNow,
            ChiefComplaint = request.ChiefComplaint,
            Symptoms = request.Symptoms,
            Temperature = request.Temperature,
            HeartRate = request.HeartRate,
            RespiratoryRate = request.RespiratoryRate,
            Weight = request.Weight,
            ClinicalNotes = request.ClinicalNotes,
            PreliminaryDiagnosis = request.PreliminaryDiagnosis,
            FinalDiagnosis = request.FinalDiagnosis,
            Severity = request.Severity,
            IcdVetCode = request.IcdVetCode,
            ExamFeeApplied = request.ExamFeeApplied,
            CheckupFeeApplied = checkupFee,
            FollowUpOfVisitId = request.FollowUpOfVisitId,
        };

        _db.Visits.Add(visit);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<VisitResponse>(visit);
    }

    public async Task<VisitResponse> UpdateAsync(Guid id, VisitPatchRequest request, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                    ?? throw new NotFoundException("visit", id);

        if (VisitStatus.IsTerminal(visit.Status))
        {
            throw new ConflictException("visit_locked",
                $"Visit '{id}' is {visit.Status}; closed visits are server-authoritative and cannot be edited.");
        }

        if (request.Status is { } target && target != visit.Status)
        {
            if (VisitStatus.IsTerminal(target))
            {
                throw new ConflictException("use_dedicated_endpoint",
                    "Close a visit via POST /visits/{id}/complete or /cancel, not PATCH.");
            }

            if (!VisitStatus.CanTransition(visit.Status, target))
            {
                throw new ConflictException("invalid_status_transition",
                    $"Cannot transition a visit from '{visit.Status}' to '{target}'.");
            }

            visit.Status = target;
            visit.StartedAt ??= _clock.UtcNow;
        }

        // M15 — (re)attribute the visit to a farm of its customer.
        if (request.FarmId is { } farmId)
        {
            await RequireFarmBelongsToCustomerAsync(farmId, visit.CustomerId, cancellationToken);
            visit.FarmId = farmId;
        }

        if (request.ChiefComplaint is not null) visit.ChiefComplaint = request.ChiefComplaint;
        if (request.Symptoms is not null) visit.Symptoms = request.Symptoms;
        if (request.Temperature.HasValue) visit.Temperature = request.Temperature;
        if (request.HeartRate.HasValue) visit.HeartRate = request.HeartRate;
        if (request.RespiratoryRate.HasValue) visit.RespiratoryRate = request.RespiratoryRate;
        if (request.Weight.HasValue) visit.Weight = request.Weight;
        if (request.ClinicalNotes is not null) visit.ClinicalNotes = request.ClinicalNotes;
        if (request.PreliminaryDiagnosis is not null) visit.PreliminaryDiagnosis = request.PreliminaryDiagnosis;
        if (request.FinalDiagnosis is not null) visit.FinalDiagnosis = request.FinalDiagnosis;
        if (request.Severity is not null) visit.Severity = request.Severity;
        if (request.IcdVetCode is not null) visit.IcdVetCode = request.IcdVetCode;
        if (request.ExamFeeApplied.HasValue) visit.ExamFeeApplied = request.ExamFeeApplied;

        // M23 — the checkup fee is re-priceable until billed (invoice line or completion backstop);
        // after that the visit's record must keep agreeing with the issued, append-only charge.
        if (request.CheckupFeeApplied.HasValue && request.CheckupFeeApplied != visit.CheckupFeeApplied)
        {
            await BilledChargeGuard.EnsureCheckupFeeNotBilledAsync(_db, visit.Id, cancellationToken);
            visit.CheckupFeeApplied = request.CheckupFeeApplied;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<VisitResponse>(visit);
    }

    public Task<VisitResponse> CompleteAsync(Guid id, CancellationToken cancellationToken)
        => CloseAsync(id, VisitStatus.Completed, cancellationToken);

    public Task<VisitResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
        => CloseAsync(id, VisitStatus.Cancelled, cancellationToken);

    private async Task<VisitResponse> CloseAsync(Guid id, string target, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                    ?? throw new NotFoundException("visit", id);

        // Idempotent: re-closing to the same terminal state is a no-op (offline replay safety).
        if (visit.Status == target)
        {
            return _mapper.Map<VisitResponse>(visit);
        }

        if (!VisitStatus.CanTransition(visit.Status, target))
        {
            throw new ConflictException("invalid_status_transition",
                $"Cannot transition a visit from '{visit.Status}' to '{target}'.");
        }

        visit.Status = target;
        visit.EndedAt = _clock.UtcNow;

        // M17 — completing an in-clinic visit posts its checkup fee (رسوم الكشف) to the owner ledger
        // (M16 routing). A waived follow-up (fee 0) or a cancellation posts nothing.
        var postCheckup = target == VisitStatus.Completed
                          && visit.VisitType == VisitType.InClinic
                          && visit.CheckupFeeApplied is > 0m;

        if (!postCheckup)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return _mapper.Map<VisitResponse>(visit);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await PostCheckupFeeAsync(visit, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return _mapper.Map<VisitResponse>(visit);
    }

    /// <summary>
    /// In-clinic checkup fee for a new visit (M17): the supplied value (editable, incl. 0 to waive)
    /// when present, else the <c>system_settings.default_checkup_fee</c> default; field visits get none.
    /// </summary>
    private async Task<decimal?> ResolveCheckupFeeAsync(
        string visitType, decimal? explicitFee, CancellationToken cancellationToken)
    {
        if (explicitFee is not null || visitType != VisitType.InClinic)
        {
            return explicitFee;
        }

        return await _db.SystemSettings.AsNoTracking()
            .Select(s => (decimal?)s.DefaultCheckupFee)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;
    }

    private async Task PostCheckupFeeAsync(Visit visit, CancellationToken cancellationToken)
    {
        var ledgerId = await _ownerLedger.ResolveAsync(visit.CustomerId, visit.FarmId, cancellationToken);
        if (ledgerId is not { } lid)
        {
            return; // defensive: an in-clinic visit always has a customer, so this never trips
        }

        await _ledgers.AppendEntryAsync(
            new LedgerEntryRequest(
                Id: null,
                LedgerId: lid,
                EntryType: LedgerEntryType.CheckupFee,
                Amount: Math.Round(visit.CheckupFeeApplied!.Value, 2, MidpointRounding.AwayFromZero),
                InvoiceId: null,
                ReceiptVoucherId: null,
                Description: visit.VisitNumber is null ? "Checkup fee" : $"Checkup fee — visit {visit.VisitNumber}",
                IdempotencyKey: $"checkup-{visit.Id}"),
            cancellationToken);
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }

    private async Task RequireFarmBelongsToCustomerAsync(Guid farmId, Guid customerId, CancellationToken cancellationToken)
    {
        var farmCustomerId = await _db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => (Guid?)f.CustomerId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("farm", farmId);

        if (farmCustomerId != customerId)
        {
            throw new ConflictException("farm_customer_mismatch",
                "The farm does not belong to the visit's customer.");
        }
    }
}
