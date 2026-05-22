using VetSystem.API.Catalog;
using VetSystem.API.Filters;
using VetSystem.Application.Catalog.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Admin;

/// <summary>Admin CRUD for the services catalog (M2). Mirrors <see cref="ProductsModule"/>.</summary>
public sealed class ServicesModule : IEndpointModule
{
    private const string EntityType = "service";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/services")
            .RequireAuthorization()
            .WithTags("Admin");

        group.MapGet("/", List)
            .WithName("Admin_Services_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Admin_Services_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.CatalogWrite)
            .AddEndpointFilter<ValidationFilter<ServiceRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Services_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.CatalogWrite)
            .AddEndpointFilter<ValidationFilter<ServicePatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Services_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.CatalogWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Services_Delete");
    }

    private static async Task<IResult> List(
        ServicesAdminService svc,
        string? search,
        string? category,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(search, category, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, ServicesAdminService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        ServiceRequest request,
        ServicesAdminService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        ServicePatchRequest request,
        ServicesAdminService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, ServicesAdminService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
