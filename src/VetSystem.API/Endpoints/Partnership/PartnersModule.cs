using VetSystem.API.Filters;
using VetSystem.API.Partnership;
using VetSystem.Application.Partnership;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Partnership;

/// <summary>
/// Partner CRUD (PRD §6.8, M10 task 2). Admin-only — the whole group is gated on
/// <see cref="PermissionKey.PartnershipManage"/>, reads included, since partner data is sensitive
/// profit-distribution config. Available only in a <c>partnership</c> environment: the service 404s in
/// a <c>solo</c> one. Not on the <c>/sync</c> path — partners are Center-Web data, never doctor-scoped.
/// </summary>
public sealed class PartnersModule : IEndpointModule
{
    private const string EntityType = "partner";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/partners")
            .RequireAuthorization()
            .WithTags("Partners");

        group.RequirePermission(PermissionKey.PartnershipManage);

        group.MapGet("/", List).WithName("Partners_List");
        group.MapGet("/{id:guid}", Get).WithName("Partners_Get");

        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<PartnerCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Partners_Create");

        group.MapPatch("/{id:guid}", Update)
            .AddEndpointFilter<ValidationFilter<PartnerPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Partners_Update");

        group.MapDelete("/{id:guid}", Delete)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Partners_Delete");
    }

    private static async Task<IResult> List(PartnersService svc, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, PartnersService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(PartnerCreateRequest request, PartnersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(Guid id, PartnerPatchRequest request, PartnersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, PartnersService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
