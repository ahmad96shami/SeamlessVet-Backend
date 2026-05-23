using VetSystem.API.Filters;
using VetSystem.API.Partnership;
using VetSystem.Application.Partnership;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Partnership;

/// <summary>
/// Partnership-share CRUD (PRD §6.8, M10 task 3). Same gate as <see cref="PartnersModule"/>:
/// Admin-only (<see cref="PermissionKey.PartnershipManage"/>), partnership-environment-only. Each
/// write re-validates the per-environment ≤ 100% invariant in the service.
/// </summary>
public sealed class PartnershipSharesModule : IEndpointModule
{
    private const string EntityType = "partnership_share";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/partnership-shares")
            .RequireAuthorization()
            .WithTags("PartnershipShares");

        group.RequirePermission(PermissionKey.PartnershipManage);

        group.MapGet("/", List).WithName("PartnershipShares_List");
        group.MapGet("/{id:guid}", Get).WithName("PartnershipShares_Get");

        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<PartnershipShareCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("PartnershipShares_Create");

        group.MapPatch("/{id:guid}", Update)
            .AddEndpointFilter<ValidationFilter<PartnershipSharePatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("PartnershipShares_Update");

        group.MapDelete("/{id:guid}", Delete)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("PartnershipShares_Delete");
    }

    private static async Task<IResult> List(
        PartnershipSharesService svc, Guid? partnerId, DateOnly? activeOn, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(partnerId, activeOn, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, PartnershipSharesService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(
        PartnershipShareCreateRequest request, PartnershipSharesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, PartnershipSharePatchRequest request, PartnershipSharesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, PartnershipSharesService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
