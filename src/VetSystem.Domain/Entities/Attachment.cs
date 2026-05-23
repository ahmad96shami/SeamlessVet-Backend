using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — an image (X-ray, before/after) or PDF (lab report) attached to a <see cref="Visit"/>
/// (PRD §5.2-F). Files live in the private R2 bucket; <see cref="Url"/> holds only the object key
/// (NULL until the direct-to-R2 upload completes — offline outbox). Read access is via short-lived
/// signed URLs minted on demand (CLAUDE.md storage rule).
/// </summary>
public sealed class Attachment : Entity
{
    public Guid VisitId { get; set; }

    public string FileType { get; set; } = AttachmentType.Photo;

    /// <summary>R2 object key; NULL until <see cref="UploadStatus"/> becomes <c>uploaded</c>.</summary>
    public string? Url { get; set; }

    public string? Title { get; set; }

    public DateOnly? DocDate { get; set; }

    public string? Description { get; set; }

    public string UploadStatus { get; set; } = Entities.UploadStatus.Pending;
}

public static class AttachmentType
{
    public const string Photo = "photo";
    public const string Pdf = "pdf";

    public static readonly IReadOnlyCollection<string> All = [Photo, Pdf];
}

public static class UploadStatus
{
    public const string Pending = "pending";
    public const string Uploaded = "uploaded";
    public const string Failed = "failed";

    public static readonly IReadOnlyCollection<string> All = [Pending, Uploaded, Failed];
}
