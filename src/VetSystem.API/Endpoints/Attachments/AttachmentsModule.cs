using VetSystem.API.Attachments;
using VetSystem.API.Filters;
using VetSystem.Application.Attachments.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Attachments;

/// <summary>
/// Attachment endpoints (PRD §5.2-F, M5 tasks 14–16). <c>presigned-upload</c> mints a signed PUT URL
/// (idempotent on the client attachment id, so no idempotency-key filter); <c>confirm</c> records
/// the upload; reads return short-lived signed GET URLs. Writes require
/// <see cref="PermissionKey.MedicalWrite"/>.
/// </summary>
public sealed class AttachmentsModule : IEndpointModule
{
    private const string EntityType = "attachment";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/attachments")
            .RequireAuthorization()
            .WithTags("Attachments");

        group.MapGet("/", List).WithName("Attachments_List");
        group.MapGet("/{id:guid}", Get).WithName("Attachments_Get");

        group.MapPost("/presigned-upload", PresignedUpload)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<PresignedUploadRequest>>()
            .WithName("Attachments_PresignedUpload");

        group.MapPatch("/{id:guid}", Confirm)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<AttachmentConfirmRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Attachments_Confirm");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Attachments_Delete");
    }

    private static async Task<IResult> List(
        AttachmentsService svc, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(visitId, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, AttachmentsService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> PresignedUpload(
        PresignedUploadRequest request, AttachmentsService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.PresignedUploadAsync(request, cancellationToken));

    private static async Task<IResult> Confirm(
        Guid id, AttachmentConfirmRequest request, AttachmentsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.ConfirmAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, AttachmentsService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
