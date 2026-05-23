namespace VetSystem.Application.Attachments.Contracts;

/// <summary>
/// SCHEMA §6 attachment flow (PRD §5.2-F). The client asks for a signed PUT URL, uploads the file
/// directly to R2, then confirms. <c>Id</c> is client-generated (Guid v7); requesting an upload URL
/// for an id that already exists just re-mints a fresh URL (safe retry) rather than duplicating.
/// </summary>
public sealed record PresignedUploadRequest(
    Guid? Id,
    Guid VisitId,
    string FileType,
    string? Title,
    DateOnly? DocDate,
    string? Description,
    string? ContentType);

/// <summary>What the client needs to upload directly to the private bucket.</summary>
public sealed record PresignedUploadResponse(
    Guid AttachmentId,
    string ObjectKey,
    string UploadUrl,
    DateTimeOffset ExpiresAt);

/// <summary>Confirms the direct-to-R2 upload. <c>UploadStatus</c> ∈ {uploaded, failed} (default uploaded).</summary>
public sealed record AttachmentConfirmRequest(string? UploadStatus);

/// <summary>
/// Attachment metadata for reads. <c>DownloadUrl</c> is a freshly-minted short-lived signed GET URL,
/// present only once the upload is confirmed; the raw object key never leaves the server.
/// </summary>
public sealed record AttachmentResponse(
    Guid Id,
    Guid VisitId,
    string FileType,
    string? Title,
    DateOnly? DocDate,
    string? Description,
    string UploadStatus,
    string? DownloadUrl,
    DateTimeOffset? DownloadUrlExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
