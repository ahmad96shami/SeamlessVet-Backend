using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// Throw-away handler for <c>/sync/sync_test_records</c> — exercises the auth → idempotency →
/// env-scoped DbContext pipeline end-to-end. Safe to delete once the first real milestone
/// (M1/M2/...) lands its own <see cref="ISyncTableHandler"/>.
/// </summary>
public sealed class SyncTestHandler : ISyncTableHandler
{
    public const string TableName = "test";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public SyncTestHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        if (_user.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var label = body.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String
            ? labelEl.GetString() ?? string.Empty
            : string.Empty;

        var record = new SyncTestRecord
        {
            Id = id,
            EnvironmentId = envId,
            Label = label,
        };

        _db.SyncTestRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(record.Id, record.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        var record = await _db.SyncTestRecords.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                     ?? throw new NotFoundException(TableName, id);

        if (body.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String)
        {
            record.Label = labelEl.GetString() ?? string.Empty;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(record.Id, record.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await _db.SyncTestRecords.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                     ?? throw new NotFoundException(TableName, id);

        _db.SyncTestRecords.Remove(record);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(record.Id, record.UpdatedAt);
    }
}
