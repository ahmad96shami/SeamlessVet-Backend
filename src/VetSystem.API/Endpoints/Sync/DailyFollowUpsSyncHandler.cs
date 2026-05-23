using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/daily_follow_ups</c> (M5 task 19). Clinic-only — a follow-up against a field visit is
/// rejected on any path.
/// </summary>
public sealed class DailyFollowUpsSyncHandler : ISyncTableHandler
{
    public const string TableName = "daily_follow_ups";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public DailyFollowUpsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        if (await _db.DailyFollowUps.IgnoreQueryFilters().AnyAsync(f => f.Id == id, cancellationToken))
        {
            throw new ConflictException("daily_follow_up_already_exists", $"Daily follow-up '{id}' already exists; use PATCH.");
        }

        var visitId = SyncBody.RequireGuid(body, "visit_id");
        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                    ?? throw new NotFoundException("visit", visitId);

        if (visit.VisitType == VisitType.Field)
        {
            throw new ConflictException("follow_up_clinic_only",
                "Daily follow-ups are for in-clinic hospitalized cases only, not field visits.");
        }

        var entry = new DailyFollowUp
        {
            Id = id,
            VisitId = visitId,
            EntryDate = SyncBody.OptionalDate(body, "entry_date")
                        ?? throw new ConflictException("invalid_payload", "'entry_date' is required and must be a date."),
            Condition = SyncBody.OptionalString(body, "condition"),
            Temperature = SyncBody.OptionalDecimal(body, "temperature"),
            HeartRate = SyncBody.OptionalInt(body, "heart_rate"),
            RespiratoryRate = SyncBody.OptionalInt(body, "respiratory_rate"),
            AdministeredMeds = SyncBody.OptionalString(body, "administered_meds"),
            Notes = SyncBody.OptionalString(body, "notes"),
        };

        _db.DailyFollowUps.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entry.Id, entry.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var entry = await _db.DailyFollowUps.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        if (body.TryGetProperty("entry_date", out _))
        {
            entry.EntryDate = SyncBody.OptionalDate(body, "entry_date")
                              ?? throw new ConflictException("invalid_payload", "'entry_date' must be a date.");
        }
        if (SyncBody.TryGetString(body, "condition", out var c)) entry.Condition = c;
        if (body.TryGetProperty("temperature", out _)) entry.Temperature = SyncBody.OptionalDecimal(body, "temperature");
        if (body.TryGetProperty("heart_rate", out _)) entry.HeartRate = SyncBody.OptionalInt(body, "heart_rate");
        if (body.TryGetProperty("respiratory_rate", out _)) entry.RespiratoryRate = SyncBody.OptionalInt(body, "respiratory_rate");
        if (SyncBody.TryGetString(body, "administered_meds", out var am)) entry.AdministeredMeds = am;
        if (SyncBody.TryGetString(body, "notes", out var n)) entry.Notes = n;

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entry.Id, entry.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var entry = await _db.DailyFollowUps.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        _db.DailyFollowUps.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entry.Id, entry.UpdatedAt);
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
