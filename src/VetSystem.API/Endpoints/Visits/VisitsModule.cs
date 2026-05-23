using VetSystem.API.Filters;
using VetSystem.API.Visits;
using VetSystem.Application.Visits.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Visits;

/// <summary>
/// Visit lifecycle endpoints (PRD §5.2, M5 tasks 4–6). Reads need only authentication; writes are
/// gated on <see cref="PermissionKey.MedicalWrite"/> and carry an idempotency key. Terminal
/// transitions are explicit endpoints (<c>/complete</c>, <c>/cancel</c>) so closing a visit — which
/// makes it server-authoritative — is an auditable single action, never a side effect of a PATCH.
/// </summary>
public sealed class VisitsModule : IEndpointModule
{
    private const string EntityType = "visit";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/visits")
            .RequireAuthorization()
            .WithTags("Visits");

        group.MapGet("/", List).WithName("Visits_List");
        group.MapGet("/{id:guid}", Get).WithName("Visits_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<VisitCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Visits_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<VisitPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Visits_Update");

        group.MapPost("/{id:guid}/complete", Complete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter("visit_complete"))
            .WithName("Visits_Complete");

        group.MapPost("/{id:guid}/cancel", Cancel)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter("visit_cancel"))
            .WithName("Visits_Cancel");
    }

    private static async Task<IResult> List(
        VisitsService svc,
        Guid? customerId,
        Guid? petId,
        Guid? doctorId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, petId, doctorId, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, VisitsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        VisitCreateRequest request,
        VisitsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        VisitPatchRequest request,
        VisitsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Complete(Guid id, VisitsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CompleteAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Cancel(Guid id, VisitsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CancelAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
