using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
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
/// is rejected. A stay is created <b>open</b>; the per-night rate is snapshotted from
/// <c>system_settings</c> by care type at creation. Closing the stay (<see cref="CloseAsync"/>) only
/// <b>records</b> the checkout and counts nights hotel-style via <see cref="NightStayChargeCalculator"/>
/// — M23 decoupled billing from close. The charge is collected on the invoice rail (a closed stay
/// auto-assembles into the visit's POS invoice, back-linked via <c>invoice_items.night_stay_id</c>),
/// with the visit-completion backstop (<see cref="CloseAndPostUnbilledForVisitAsync"/>) posting a
/// <c>night_stay</c> ledger entry for whatever never reached the till (idempotent
/// <c>night-stay-{id}</c>). Until billed by either writer a stay — open or closed — can be edited,
/// re-closed (recompute), or deleted; once billed it is frozen (<see cref="BilledChargeGuard"/>).
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
        if (request.Notes is not null) stay.Notes = request.Notes;

        if (billingEdit && stay.CheckOutAt is not null)
        {
            await RecomputeClosedTotalsAsync(stay, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<NightStayResponse>(stay);
    }

    /// <summary>
    /// Records the checkout and computes nights/total — M23: no charge posts here (billing moved to
    /// the invoice rail / completion backstop). A replayed close (no explicit checkout) of an
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
    /// M23 completion backstop — called by <c>VisitsService.CloseAsync</c> inside its transaction
    /// (shared scoped DbContext). Auto-closes any still-open stay (checkout = now) and posts the
    /// <c>night_stay</c> ledger entry for every stay not already billed by an invoice line. The
    /// ledger key (<c>night-stay-{id}</c>) absorbs replays.
    /// </summary>
    public async Task CloseAndPostUnbilledForVisitAsync(Visit visit, CancellationToken cancellationToken)
    {
        var stays = await _db.NightStays.Where(n => n.VisitId == visit.Id).ToListAsync(cancellationToken);
        if (stays.Count == 0)
        {
            return;
        }

        foreach (var stay in stays.Where(s => s.CheckOutAt is null))
        {
            // No invalid-window rejection here: a completion minutes after check-in is legitimate
            // (the calculator clamps to 0 nights and PostChargeAsync skips zero totals).
            stay.CheckOutAt = Max(_clock.UtcNow, stay.CheckInAt);
            await RecomputeClosedTotalsAsync(stay, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var stayIds = stays.Select(s => s.Id).ToList();
        var billedOnInvoice = (await _db.InvoiceItems.AsNoTracking()
                .Where(it => it.NightStayId != null && stayIds.Contains(it.NightStayId!.Value))
                .Select(it => it.NightStayId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var stay in stays.Where(s => !billedOnInvoice.Contains(s.Id)))
        {
            await PostChargeAsync(stay, visit, cancellationToken);
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
