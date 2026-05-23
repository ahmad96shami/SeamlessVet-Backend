using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Attachments.Contracts;
using VetSystem.Application.Common;
using VetSystem.Application.Storage;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Attachments;

/// <summary>
/// Attachment lifecycle (PRD §5.2-F, M5 tasks 14–16). Files live in the private R2 bucket; the DB
/// row holds only the object key. Flow: <c>presigned-upload</c> creates the row (status
/// <c>pending</c>) and returns a signed PUT URL the client uploads to directly; <c>confirm</c> flips
/// it to <c>uploaded</c> and records the key; reads return a fresh short-lived signed GET URL. The
/// object key is derived server-side from the attachment id, so a client can never point a row at
/// another object.
/// </summary>
public sealed class AttachmentsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly ISignedUrlService _signer;

    public AttachmentsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        ISignedUrlService signer)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _signer = signer;
    }

    public async Task<PresignedUploadResponse> PresignedUploadAsync(
        PresignedUploadRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireVisitAsync(request.VisitId, cancellationToken);

        // Safe retry: requesting an upload URL for an existing attachment re-mints rather than
        // duplicating. A fresh id creates the pending row.
        var attachment = request.Id is { } id && id != Guid.Empty
            ? await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            : null;

        if (attachment is null)
        {
            attachment = new Attachment
            {
                Id = request.Id ?? Guid.Empty,
                VisitId = request.VisitId,
                FileType = request.FileType,
                Title = request.Title,
                DocDate = request.DocDate,
                Description = request.Description,
                UploadStatus = UploadStatus.Pending,
                Url = null,
            };
            _db.Attachments.Add(attachment);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var objectKey = ObjectKey(attachment);
        var put = await _signer.MintPutAsync(objectKey, request.ContentType);
        return new PresignedUploadResponse(attachment.Id, objectKey, put.Url, put.ExpiresAt);
    }

    public async Task<AttachmentResponse> ConfirmAsync(
        Guid id, AttachmentConfirmRequest request, CancellationToken cancellationToken)
    {
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                         ?? throw new NotFoundException("attachment", id);

        var status = request.UploadStatus ?? UploadStatus.Uploaded;
        if (status == UploadStatus.Uploaded)
        {
            attachment.Url = ObjectKey(attachment);
            attachment.UploadStatus = UploadStatus.Uploaded;
        }
        else
        {
            attachment.UploadStatus = UploadStatus.Failed;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<AttachmentResponse>(attachment);
    }

    public async Task<AttachmentResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                         ?? throw new NotFoundException("attachment", id);

        var response = _mapper.Map<AttachmentResponse>(attachment);
        if (attachment.UploadStatus == UploadStatus.Uploaded && attachment.Url is not null)
        {
            var get = await _signer.MintGetAsync(attachment.Url);
            response = response with { DownloadUrl = get.Url, DownloadUrlExpiresAt = get.ExpiresAt };
        }

        return response;
    }

    public async Task<IReadOnlyList<AttachmentResponse>> ListAsync(
        Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.Attachments.AsNoTracking();
        if (visitId is { } vid) query = query.Where(a => a.VisitId == vid);

        var rows = await query
            .OrderBy(a => a.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        // List is metadata only — callers GET a single attachment to obtain a signed download URL.
        return rows.Select(_mapper.Map<AttachmentResponse>).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                         ?? throw new NotFoundException("attachment", id);

        _db.Attachments.Remove(attachment);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deterministic, server-controlled key: <c>attachments/{env}/{visit}/{attachmentId}</c>.</summary>
    private static string ObjectKey(Attachment attachment)
        => $"attachments/{attachment.EnvironmentId}/{attachment.VisitId}/{attachment.Id}";

    private async Task RequireVisitAsync(Guid visitId, CancellationToken cancellationToken)
    {
        if (!await _db.Visits.AnyAsync(v => v.Id == visitId, cancellationToken))
        {
            throw new NotFoundException("visit", visitId);
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
