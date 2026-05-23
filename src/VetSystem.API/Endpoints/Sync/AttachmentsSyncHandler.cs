using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/attachments</c> (M5 task 19). The offline outbox: the device creates the attachment row
/// (typically <c>pending</c>, <c>url</c> null) and later — once online — uploads to R2 and PATCHes
/// the object key + <c>upload_status</c>. The presigned-URL minting itself is the online-only
/// <c>/attachments/presigned-upload</c> path; this handler just persists the record.
/// </summary>
public sealed class AttachmentsSyncHandler : ISyncTableHandler
{
    public const string TableName = "attachments";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public AttachmentsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        if (await _db.Attachments.IgnoreQueryFilters().AnyAsync(a => a.Id == id, cancellationToken))
        {
            throw new ConflictException("attachment_already_exists", $"Attachment '{id}' already exists; use PATCH.");
        }

        var visitId = SyncBody.RequireGuid(body, "visit_id");
        await EnsureExistsAsync(_db.Visits.AnyAsync(v => v.Id == visitId, cancellationToken), "visit", visitId);

        var attachment = new Attachment
        {
            Id = id,
            VisitId = visitId,
            FileType = SyncBody.RequireString(body, "file_type", AttachmentType.All, TableName),
            Url = SyncBody.OptionalString(body, "url"),
            Title = SyncBody.OptionalString(body, "title"),
            DocDate = SyncBody.OptionalDate(body, "doc_date"),
            Description = SyncBody.OptionalString(body, "description"),
            UploadStatus = SyncBody.OptionalString(body, "upload_status") is { } s && UploadStatus.All.Contains(s)
                ? s
                : UploadStatus.Pending,
        };

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(attachment.Id, attachment.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                         ?? throw new NotFoundException(TableName, id);

        if (SyncBody.TryGetString(body, "url", out var url)) attachment.Url = url;
        if (SyncBody.TryGetString(body, "title", out var title)) attachment.Title = title;
        if (body.TryGetProperty("doc_date", out _)) attachment.DocDate = SyncBody.OptionalDate(body, "doc_date");
        if (SyncBody.TryGetString(body, "description", out var desc)) attachment.Description = desc;
        if (SyncBody.TryGetString(body, "upload_status", out var us) && us is not null)
        {
            if (!UploadStatus.All.Contains(us))
            {
                throw new ConflictException("invalid_upload_status", $"upload_status '{us}' is not valid.");
            }
            attachment.UploadStatus = us;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(attachment.Id, attachment.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                         ?? throw new NotFoundException(TableName, id);

        _db.Attachments.Remove(attachment);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(attachment.Id, attachment.UpdatedAt);
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
}
