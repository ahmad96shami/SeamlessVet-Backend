using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Application.NightStays.Contracts;
using VetSystem.Application.Settings;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.NightStays;

/// <summary>
/// Night-stay (مبيت, PRD §18.6, M17 task 6) CRUD + close. Clinic-only — a stay against a field visit
/// is rejected. A stay is created <b>open</b>; the per-night rate is snapshotted from
/// <c>system_settings</c> by care type at creation. Closing the stay (<see cref="CloseAsync"/>) only
/// <b>records</b> the checkout and counts nights hotel-style via <see cref="NightStayChargeCalculator"/>
/// — M23 decoupled billing from close. The charge is collected on the invoice rail (a closed stay
/// auto-assembles into the visit's POS invoice, back-linked via <c>invoice_items.night_stay_id</c>);
/// visit completion never posts to the ledger — it only auto-closes a still-open stay
/// (<see cref="CloseOpenForVisitAsync"/>) so the POS can bill it. Until billed by an invoice line a
/// stay — open or closed — can be edited, re-closed (recompute), or deleted; once billed it is
/// frozen (<see cref="BilledChargeGuard"/>).
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

    public NightStaysService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
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

        var billed = await LoadBilledStayIdsAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
        return rows
            .Select(r => _mapper.Map<NightStayResponse>(r) with { Billed = billed.Contains(r.Id) })
            .ToList();
    }

    public async Task<NightStayResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stay = await _db.NightStays.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);
        var billed = await LoadBilledStayIdsAsync(new[] { stay.Id }, cancellationToken);
        return _mapper.Map<NightStayResponse>(stay) with { Billed = billed.Contains(stay.Id) };
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
            ExitHour = request.ExitHour,
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

        // M23 — billing fields stay editable until the stay is BILLED (invoice line or completion
        // backstop), not until it is merely closed; a closed-unbilled edit recomputes nights/total.
        var billingEdit = request.CareType is not null || request.CheckInAt is not null || request.NightlyRate is not null;
        if (billingEdit)
        {
            await BilledChargeGuard.EnsureNightStayNotBilledAsync(_db, id, cancellationToken);
        }

        if (request.CareType is not null) stay.CareType = request.CareType;
        if (request.CheckInAt is { } checkIn) stay.CheckInAt = checkIn;
        if (request.NightlyRate is { } nr) stay.NightlyRate = Money(nr);
        // Exit hour is informational (never bills), so it stays editable even on a closed/billed stay.
        if (request.ExitHour is { } eh) stay.ExitHour = eh;
        if (request.Notes is not null) stay.Notes = request.Notes;

        if (billingEdit && stay.CheckOutAt is not null)
        {
            await RecomputeClosedTotalsAsync(stay, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<NightStayResponse>(stay);
    }

    /// <summary>
    /// Records the checkout and computes nights/total — M23: no charge posts here (billing happens
    /// on the POS invoice rail). A replayed close (no explicit checkout) of an
    /// already-closed stay is an idempotent no-op; a close with an <b>explicit</b> checkout on a
    /// closed-unbilled stay deliberately re-closes it (recompute) — that's how a mistaken checkout
    /// is corrected before billing.
    /// </summary>
    public async Task<NightStayResponse> CloseAsync(Guid id, NightStayCloseRequest request, CancellationToken cancellationToken)
    {
        var stay = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);

        if (stay.CheckOutAt is not null)
        {
            if (request.CheckOutAt is null)
            {
                return _mapper.Map<NightStayResponse>(stay); // idempotent replay
            }

            await BilledChargeGuard.EnsureNightStayNotBilledAsync(_db, id, cancellationToken);
        }

        var checkOut = request.CheckOutAt ?? _clock.UtcNow;
        if (checkOut <= stay.CheckInAt)
        {
            throw new ConflictException("night_stay_invalid_window", "Check-out must be after check-in.");
        }

        stay.CheckOutAt = checkOut;
        await RecomputeClosedTotalsAsync(stay, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<NightStayResponse>(stay);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var stay = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                   ?? throw new NotFoundException("night_stay", id);

        // M23 — deletable until billed (open OR closed); a billed stay backs an invoice line /
        // posted backstop charge.
        await BilledChargeGuard.EnsureNightStayNotBilledAsync(_db, id, cancellationToken);

        _db.NightStays.Remove(stay); // soft-delete via the auditing interceptor
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Called by <c>VisitsService.CloseAsync</c> when a visit completes (shared scoped DbContext):
    /// auto-closes any still-open stay (checkout = now) so its nights/total freeze and it becomes
    /// billable on the POS invoice rail. M23/bugfix — completion posts NOTHING to the ledger;
    /// boarding charges are collected exclusively through the POS. Mutates tracked entities; the
    /// caller's <c>SaveChangesAsync</c> persists them.
    /// </summary>
    public async Task CloseOpenForVisitAsync(Visit visit, CancellationToken cancellationToken)
    {
        var openStays = await _db.NightStays
            .Where(n => n.VisitId == visit.Id && n.CheckOutAt == null)
            .ToListAsync(cancellationToken);

        foreach (var stay in openStays)
        {
            // No invalid-window rejection: a completion minutes after check-in is legitimate
            // (the calculator clamps to 0 nights — a zero-night stay simply has nothing to bill).
            stay.CheckOutAt = Max(_clock.UtcNow, stay.CheckInAt);
            await RecomputeClosedTotalsAsync(stay, cancellationToken);
        }
    }

    /// <summary>
    /// M23 — the subset of <paramref name="stayIds"/> already billed by either writer: a POS invoice
    /// line (<c>invoice_items.night_stay_id</c>) or the completion backstop's ledger entry
    /// (<c>night-stay-{id}</c>). Mirrors <see cref="BilledChargeGuard.EnsureNightStayNotBilledAsync"/>
    /// exactly, so the response's <c>Billed</c> flag matches the freeze rule the UI must obey.
    /// </summary>
    private async Task<HashSet<Guid>> LoadBilledStayIdsAsync(
        IReadOnlyCollection<Guid> stayIds, CancellationToken cancellationToken)
    {
        if (stayIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var billed = (await _db.InvoiceItems.AsNoTracking()
                .Where(it => it.NightStayId != null && stayIds.Contains(it.NightStayId!.Value))
                .Select(it => it.NightStayId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var keys = stayIds.Select(id => $"night-stay-{id}").ToList();
        var billedKeys = (await _db.LedgerEntries.AsNoTracking()
                .Where(e => keys.Contains(e.IdempotencyKey))
                .Select(e => e.IdempotencyKey)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        foreach (var id in stayIds)
        {
            if (billedKeys.Contains($"night-stay-{id}"))
            {
                billed.Add(id);
            }
        }

        return billed;
    }

    /// <summary>Recounts nights/total from the stay's current window + rate (closed stays only).</summary>
    private async Task RecomputeClosedTotalsAsync(NightStay stay, CancellationToken cancellationToken)
    {
        if (stay.CheckOutAt is not { } checkOut)
        {
            return;
        }

        var settings = await LoadNightStaySettingsAsync(RequireEnvironment(), cancellationToken);
        stay.NightsCount = NightStayChargeCalculator.CountNights(
            ToClinicLocal(stay.CheckInAt),
            ToClinicLocal(checkOut),
            new TimeOnly(settings.CheckoutHour, 0));
        stay.Total = Money(stay.NightsCount * stay.NightlyRate);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

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
