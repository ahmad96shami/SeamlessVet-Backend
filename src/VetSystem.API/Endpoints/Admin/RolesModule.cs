using VetSystem.API.Filters;
using VetSystem.API.Roles;
using VetSystem.Application.Roles.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Admin;

/// <summary>
/// Admin Roles tab: create custom roles, edit any role's permissions (admin role protected), delete
/// custom roles, and read the permission catalog for the editor. All gated on
/// <see cref="PermissionKey.RolesManage"/>. Online-only center-web.
/// </summary>
public sealed class RolesModule : IEndpointModule
{
    private const string EntityType = "role";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/roles")
            .RequireAuthorization()
            .WithTags("Admin");

        group.MapGet("/", List)
            .RequirePermission(PermissionKey.RolesManage)
            .WithName("Admin_Roles_List");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.RolesManage)
            .AddEndpointFilter<ValidationFilter<CreateRoleRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Roles_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.RolesManage)
            .AddEndpointFilter<ValidationFilter<UpdateRoleRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Roles_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.RolesManage)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Roles_Delete");

        var perms = endpoints.MapGroup("/admin/permissions")
            .RequireAuthorization()
            .WithTags("Admin");

        perms.MapGet("/", PermissionCatalog)
            .RequirePermission(PermissionKey.RolesManage)
            .WithName("Admin_Permissions_Catalog");
    }

    private static async Task<IResult> List(RoleAdminService svc, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> PermissionCatalog(RoleAdminService svc, CancellationToken cancellationToken)
    {
        var items = await svc.GetPermissionCatalogAsync(cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Create(
        CreateRoleRequest request, RoleAdminService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Update(
        Guid id, UpdateRoleRequest request, RoleAdminService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Delete(Guid id, RoleAdminService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
