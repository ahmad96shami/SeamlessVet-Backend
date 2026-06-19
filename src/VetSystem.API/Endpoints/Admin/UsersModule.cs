using VetSystem.API.Filters;
using VetSystem.API.Identity;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Admin;

public sealed class UsersModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/users")
            .RequireAuthorization()
            .WithTags("Admin");

        group.MapGet("/", List)
            .RequirePermission(PermissionKey.UsersManage)
            .WithName("Admin_Users_List");

        group.MapGet("/{id:guid}", Get)
            .RequirePermission(PermissionKey.UsersManage)
            .WithName("Admin_Users_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.UsersManage)
            .AddEndpointFilter<ValidationFilter<CreateUserRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("user_create"))
            .WithName("Admin_Users_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.UsersManage)
            .AddEndpointFilter<ValidationFilter<UpdateUserRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("user_update"))
            .WithName("Admin_Users_Update");

        group.MapPost("/{id:guid}/deactivate", Deactivate)
            .RequirePermission(PermissionKey.UsersManage)
            .AddEndpointFilter(new IdempotencyKeyFilter("user_deactivate"))
            .WithName("Admin_Users_Deactivate");

        group.MapPost("/{id:guid}/reactivate", Reactivate)
            .RequirePermission(PermissionKey.UsersManage)
            .AddEndpointFilter(new IdempotencyKeyFilter("user_reactivate"))
            .WithName("Admin_Users_Reactivate");

        group.MapPost("/{id:guid}/permission-overrides", AddPermissionOverride)
            .RequirePermission(PermissionKey.UsersPermissionsOverride)
            .AddEndpointFilter<ValidationFilter<PermissionOverrideRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("user_permission_override"))
            .WithName("Admin_Users_PermissionOverride");
    }

    private static async Task<IResult> List(
        UserAdminService admin,
        string? search,
        string? role,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await admin.ListUsersAsync(search, role, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, UserAdminService admin, CancellationToken cancellationToken)
    {
        var user = await admin.GetUserAsync(id, cancellationToken);
        return TypedResults.Ok(user);
    }

    private static async Task<IResult> Create(
        CreateUserRequest request,
        UserAdminService admin,
        CancellationToken cancellationToken)
    {
        var user = await admin.CreateUserAsync(request, cancellationToken);
        return TypedResults.Ok(user);
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateUserRequest request,
        UserAdminService admin,
        CancellationToken cancellationToken)
    {
        var user = await admin.UpdateUserAsync(id, request, cancellationToken);
        return TypedResults.Ok(user);
    }

    private static async Task<IResult> Deactivate(Guid id, UserAdminService admin, CancellationToken cancellationToken)
    {
        await admin.DeactivateAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Reactivate(Guid id, UserAdminService admin, CancellationToken cancellationToken)
    {
        await admin.ReactivateAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> AddPermissionOverride(
        Guid id,
        PermissionOverrideRequest request,
        UserAdminService admin,
        CancellationToken cancellationToken)
    {
        await admin.AddPermissionOverrideAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}
