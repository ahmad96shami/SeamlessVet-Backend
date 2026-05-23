using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/procedures</c> (M5 task 19). Persists the device's procedure record as-is — the price
/// was snapshotted on the device at the time of the visit, so the server does not re-derive it.
/// </summary>
public sealed class ProceduresSyncHandler : ISyncTableHandler
{
    public const string TableName = "procedures";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ProceduresSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        await EnsureNewAsync(_db.Procedures.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken), id);

        var visitId = SyncBody.RequireGuid(body, "visit_id");
        await EnsureExistsAsync(_db.Visits.AnyAsync(v => v.Id == visitId, cancellationToken), "visit", visitId);

        var serviceId = SyncBody.OptionalGuid(body, "service_id");
        if (serviceId is { } sid)
        {
            await EnsureExistsAsync(_db.Services.AnyAsync(s => s.Id == sid, cancellationToken), "service", sid);
        }

        var procedure = new Procedure
        {
            Id = id,
            VisitId = visitId,
            ServiceId = serviceId,
            ResultText = SyncBody.OptionalString(body, "result_text"),
            ResultFileUrl = SyncBody.OptionalString(body, "result_file_url"),
            Price = SyncBody.OptionalDecimal(body, "price") ?? 0m,
        };

        _db.Procedures.Add(procedure);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(procedure.Id, procedure.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                        ?? throw new NotFoundException(TableName, id);

        if (body.TryGetProperty("service_id", out _))
        {
            var serviceId = SyncBody.OptionalGuid(body, "service_id");
            if (serviceId is { } sid)
            {
                await EnsureExistsAsync(_db.Services.AnyAsync(s => s.Id == sid, cancellationToken), "service", sid);
            }
            procedure.ServiceId = serviceId;
        }

        if (SyncBody.TryGetString(body, "result_text", out var rt)) procedure.ResultText = rt;
        if (SyncBody.TryGetString(body, "result_file_url", out var rf)) procedure.ResultFileUrl = rf;
        if (body.TryGetProperty("price", out _)) procedure.Price = SyncBody.OptionalDecimal(body, "price") ?? 0m;

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(procedure.Id, procedure.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var procedure = await _db.Procedures.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                        ?? throw new NotFoundException(TableName, id);

        _db.Procedures.Remove(procedure);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(procedure.Id, procedure.UpdatedAt);
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }

    private static async Task EnsureNewAsync(Task<bool> existsQuery, Guid id)
    {
        if (await existsQuery)
        {
            throw new ConflictException("procedure_already_exists", $"Procedure '{id}' already exists; use PATCH.");
        }
    }
}
