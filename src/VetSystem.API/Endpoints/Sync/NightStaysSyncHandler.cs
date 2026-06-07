using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Application.Settings;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/night_stays</c> (مبيت, M17). Clinic-only — a stay against a field visit is rejected.
/// Sync writes persist <b>open</b> stays only: the per-night rate is server-snapshotted by care type
/// and the money fields (<c>nights_count</c>, <c>total</c>) are server-owned. Closing a stay (which
/// posts the boarding charge) is an online, money-critical operation that goes through
/// <c>POST /night-stays/{id}/close</c>, not the sync path — so <c>check_out_at</c> is rejected here.
/// </summary>
public sealed class NightStaysSyncHandler : ISyncTableHandler
{
    public const string TableName = "night_stays";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public NightStaysSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        var envId = RequireAuthenticated();

        if (await _db.NightStays.IgnoreQueryFilters().AnyAsync(n => n.Id == id, cancellationToken))
        {
            throw new ConflictException("night_stay_already_exists", $"Night stay '{id}' already exists; use PATCH.");
        }

        RejectCheckOut(body);

        var visitId = SyncBody.RequireGuid(body, "visit_id");
        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                    ?? throw new NotFoundException("visit", visitId);

        if (visit.VisitType == VisitType.Field)
        {
            throw new ConflictException("night_stay_clinic_only",
                "Night stays are for in-clinic hospitalized cases only, not field visits.");
        }

        var careType = SyncBody.RequireString(body, "care_type", CareType.All, TableName);
        var rate = SyncBody.OptionalDecimal(body, "nightly_rate")
                   ?? (await LoadNightStaySettingsAsync(envId, cancellationToken)).RateFor(careType);

        var entry = new NightStay
        {
            Id = id,
            VisitId = visitId,
            CareType = careType,
            CheckInAt = SyncBody.OptionalDateTime(body, "check_in_at") ?? throw new ConflictException(
                "invalid_payload", "'check_in_at' is required and must be an ISO-8601 timestamp."),
            CheckOutAt = null,
            NightsCount = 0,
            NightlyRate = Money(rate),
            Total = 0m,
            Notes = SyncBody.OptionalString(body, "notes"),
        };

        _db.NightStays.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entry.Id, entry.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var entry = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        RejectCheckOut(body);

        // M23 — REST allows closed-unbilled edits (with recompute); sync stays conservative: closing
        // is online-only, so a device never legitimately holds a closed stay to edit. Editing a
        // closed row here would also need the nights/total recompute this handler doesn't carry.
        if (entry.CheckOutAt is not null)
        {
            throw new ConflictException("night_stay_closed",
                "A closed night stay can't be edited via sync; use the online night-stay endpoints.");
        }

        if (body.TryGetProperty("care_type", out _))
        {
            entry.CareType = SyncBody.RequireString(body, "care_type", CareType.All, TableName);
        }
        if (body.TryGetProperty("check_in_at", out _))
        {
            entry.CheckInAt = SyncBody.OptionalDateTime(body, "check_in_at")
                              ?? throw new ConflictException("invalid_payload", "'check_in_at' must be an ISO-8601 timestamp.");
        }
        if (body.TryGetProperty("nightly_rate", out _))
        {
            entry.NightlyRate = Money(SyncBody.OptionalDecimal(body, "nightly_rate") ?? entry.NightlyRate);
        }
        if (SyncBody.TryGetString(body, "notes", out var notes)) entry.Notes = notes;

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entry.Id, entry.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var entry = await _db.NightStays.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        // M23 — deletable until billed (mirror the REST rule): the billed state, not the closed
        // state, is what backs an invoice line / posted backstop charge.
        await BilledChargeGuard.EnsureNightStayNotBilledAsync(_db, id, cancellationToken);

        _db.NightStays.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entry.Id, entry.UpdatedAt);
    }

    private static void RejectCheckOut(JsonElement body)
    {
        if (body.TryGetProperty("check_out_at", out var el) && el.ValueKind is not JsonValueKind.Null)
        {
            throw new ConflictException("night_stay_close_online_only",
                "Close a night stay via POST /night-stays/{id}/close — the charge posts online, not over sync.");
        }
    }

    private async Task<NightStaySettings> LoadNightStaySettingsAsync(Guid envId, CancellationToken cancellationToken)
    {
        var extra = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.EnvironmentId == envId)
            .Select(s => s.Extra)
            .FirstOrDefaultAsync(cancellationToken);
        return NightStaySettings.FromExtra(extra);
    }

    private Guid RequireAuthenticated()
    {
        if (_user.EnvironmentId is not { } envId || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return envId;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
