using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Application.NightStays.Contracts;
using VetSystem.Application.Settings;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.NightStays;

/// <summary>
/// Night-stay (مبيت, PRD §18.6, M17 task 6) CRUD + close. Clinic-only — a stay against a field visit
/// is rejected. A stay is created <b>open</b> (no check-out, nothing billed); the per-night rate is
/// snapshotted from <c>system_settings</c> by care type at creation. Closing the stay
/// (<see cref="CloseAsync"/>) counts nights hotel-style via <see cref="NightStayChargeCalculator"/>
/// and posts a single <c>night_stay</c> ledger entry (<c>nights × rate</c>) to the owner ledger —
/// the farm ledger for a farm-scoped visit, else the customer ledger (M16 routing). The charge is
/// idempotent (<c>night-stay-{id}</c>) and append-only: once closed, billing fields are frozen.
/// </summary>
public sealed class NightStaysService
{
    private const int MaxPageSize = 200;

    // Single-region (Palestine) deployment: the checkout-hour boundary is a wall-clock local time,
    // so stored (UTC) instants are converted to the clinic zone before the day-count. The hour is
    // configurable (system_settings); the zone is fixed to the clinic's region.
    private static readonly TimeZoneInfo ClinicZone = ResolveClinicZone();

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly ILedgerService _ledgers;
    private readonly IOwnerLedgerResolver _ownerLedger;

    public NightStaysService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        ILedgerService ledgers,
        IOwnerLedgerResolver ownerLedger)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _ledgers = ledgers;
        _ownerLedger = ownerLedger;
    }

    public async Task<IReadOnlyList<NightStayResponse>> ListAsync(
        Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.NightStays.AsNoTracking();
        if (visitId is { } vid) query = query.Where(n => n.VisitId == vid);

        var rows = await query
            .OrderBy(n => n.CheckInAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<NightStayResponse>).ToList();
    }

    public async Task<NightStayResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stay = await _db.NightStays.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);
        return _mapper.Map<NightStayResponse>(stay);
    }

    public async Task<NightStayResponse> CreateAsync(NightStayCreateRequest request, CancellationToken cancellationToken)
    {
        var envId = RequireEnvironment();

        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VisitId, cancellationToken)
                    ?? throw new NotFoundException("visit", request.VisitId);

        if (visit.VisitType == VisitType.Field)
        {
            throw new ConflictException("night_stay_clinic_only",
                "Night stays are for in-clinic hospitalized cases only, not field visits.");
        }

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.NightStays.IgnoreQueryFilters().AnyAsync(n => n.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("night_stay_id_collision", $"A night stay with id '{id}' already exists.");
            }
        }

        var rate = request.NightlyRate ?? (await LoadNightStaySettingsAsync(envId, cancellationToken)).RateFor(request.CareType);

        var stay = new NightStay
        {
            Id = request.Id ?? Guid.Empty,
            VisitId = request.VisitId,
            CareType = request.CareType,
            CheckInAt = request.CheckInAt ?? _clock.UtcNow,
            CheckOutAt = null,
            NightsCount = 0,
            NightlyRate = Money(rate),
            Total = 0m,
            Notes = request.Notes,
        };

        _db.NightStays.Add(stay);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<NightStayResponse>(stay);
    }

    public async Task<NightStayResponse> UpdateAsync(Guid id, NightStayPatchRequest request, CancellationToken cancellationToken)
    {
        var stay = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);

        var billingEdit = request.CareType is not null || request.CheckInAt is not null || request.NightlyRate is not null;
        if (stay.CheckOutAt is not null && billingEdit)
        {
            throw new ConflictException("night_stay_closed",
                "A closed (charged) night stay can't be re-priced; post a ledger adjustment instead.");
        }

        if (request.CareType is not null) stay.CareType = request.CareType;
        if (request.CheckInAt is { } checkIn) stay.CheckInAt = checkIn;
        if (request.NightlyRate is { } nr) stay.NightlyRate = Money(nr);
        if (request.Notes is not null) stay.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<NightStayResponse>(stay);
    }

    /// <summary>
    /// Closes the stay and posts the boarding charge. Idempotent: closing an already-closed stay
    /// returns it unchanged (the ledger post is also keyed by the stay id, so a retried close never
    /// double-bills).
    /// </summary>
    public async Task<NightStayResponse> CloseAsync(Guid id, NightStayCloseRequest request, CancellationToken cancellationToken)
    {
        var envId = RequireEnvironment();

        var stay = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);

        if (stay.CheckOutAt is not null)
        {
            return _mapper.Map<NightStayResponse>(stay);
        }

        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == stay.VisitId, cancellationToken)
                    ?? throw new NotFoundException("visit", stay.VisitId);

        var checkOut = request.CheckOutAt ?? _clock.UtcNow;
        if (checkOut <= stay.CheckInAt)
        {
            throw new ConflictException("night_stay_invalid_window", "Check-out must be after check-in.");
        }

        var settings = await LoadNightStaySettingsAsync(envId, cancellationToken);
        var nights = NightStayChargeCalculator.CountNights(
            ToClinicLocal(stay.CheckInAt),
            ToClinicLocal(checkOut),
            new TimeOnly(settings.CheckoutHour, 0));

        stay.CheckOutAt = checkOut;
        stay.NightsCount = nights;
        stay.Total = Money(nights * stay.NightlyRate);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await PostChargeAsync(stay, visit, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return _mapper.Map<NightStayResponse>(stay);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var stay = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);

        if (stay.CheckOutAt is not null)
        {
            throw new ConflictException("night_stay_closed",
                "A closed (charged) night stay can't be deleted; post a ledger adjustment instead.");
        }

        _db.NightStays.Remove(stay); // soft-delete via the auditing interceptor
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Posts the boarding charge to the visit's owner ledger (M16 routing). Skips a zero total.</summary>
    private async Task PostChargeAsync(NightStay stay, Visit visit, CancellationToken cancellationToken)
    {
        if (stay.Total <= 0m)
        {
            return; // zero nights or a zero rate — nothing to bill
        }

        var ledgerId = await _ownerLedger.ResolveAsync(visit.CustomerId, visit.FarmId, cancellationToken);
        if (ledgerId is not { } lid)
        {
            return; // defensive: a clinic visit always has a customer, so this never trips
        }

        await _ledgers.AppendEntryAsync(
            new LedgerEntryRequest(
                Id: null,
                LedgerId: lid,
                EntryType: LedgerEntryType.NightStay,
                Amount: stay.Total,
                InvoiceId: null,
                ReceiptVoucherId: null,
                Description: $"Night stay ({stay.CareType}) × {stay.NightsCount}",
                IdempotencyKey: $"night-stay-{stay.Id}"),
            cancellationToken);
    }

    private async Task<NightStaySettings> LoadNightStaySettingsAsync(Guid envId, CancellationToken cancellationToken)
    {
        var extra = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.EnvironmentId == envId)
            .Select(s => s.Extra)
            .FirstOrDefaultAsync(cancellationToken);
        return NightStaySettings.FromExtra(extra);
    }

    private Guid RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return envId;
    }

    private static DateTime ToClinicLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, ClinicZone).DateTime;

    private static TimeZoneInfo ResolveClinicZone()
    {
        foreach (var id in new[] { "Asia/Hebron", "Asia/Jerusalem", "Israel" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("clinic-fixed-utc+2", TimeSpan.FromHours(2), "Clinic", "Clinic");
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
