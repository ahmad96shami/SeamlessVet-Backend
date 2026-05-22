using VetSystem.API.Catalog;
using VetSystem.API.Filters;
using VetSystem.Application.Catalog.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Admin;

/// <summary>
/// Admin CRUD for the products catalog (PRD §5.5, M2). Idempotency-key required on mutations;
/// reads scope to the current environment via the global EF query filter.
/// </summary>
public sealed class ProductsModule : IEndpointModule
{
    private const string EntityType = "product";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/products")
            .RequireAuthorization()
            .WithTags("Admin");

        group.MapGet("/", List)
            .WithName("Admin_Products_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Admin_Products_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.CatalogWrite)
            .AddEndpointFilter<ValidationFilter<ProductRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Products_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.CatalogWrite)
            .AddEndpointFilter<ValidationFilter<ProductPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Products_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.CatalogWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Products_Delete");
    }

    private static async Task<IResult> List(
        ProductsAdminService svc,
        string? search,
        string? category,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(search, category, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, ProductsAdminService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        ProductRequest request,
        ProductsAdminService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        ProductPatchRequest request,
        ProductsAdminService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, ProductsAdminService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
