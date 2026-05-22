using VetSystem.API.Filters;
using VetSystem.API.Identity;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Admin;

public sealed class RegistrationRequestsModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/registration-requests")
            .RequireAuthorization()
            .RequirePermission(PermissionKey.UsersApprove)
            .WithTags("Admin");

        group.MapGet("/", List)
            .WithName("Admin_RegistrationRequests_List");

        group.MapPost("/{id:guid}/approve", Approve)
            .AddEndpointFilter<ValidationFilter<ApproveRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("registration_request_approve"))
            .WithName("Admin_RegistrationRequests_Approve");

        group.MapPost("/{id:guid}/reject", Reject)
            .AddEndpointFilter<ValidationFilter<RejectRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("registration_request_reject"))
            .WithName("Admin_RegistrationRequests_Reject");
    }

    private static async Task<IResult> List(
        UserAdminService admin,
        string? status,
        CancellationToken cancellationToken)
    {
        var items = await admin.ListPendingAsync(status, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Approve(
        Guid id,
        ApproveRequest request,
        UserAdminService admin,
        CancellationToken cancellationToken)
    {
        var summary = await admin.ApproveAsync(id, request, cancellationToken);
        return TypedResults.Ok(summary);
    }

    private static async Task<IResult> Reject(
        Guid id,
        RejectRequest request,
        UserAdminService admin,
        CancellationToken cancellationToken)
    {
        var summary = await admin.RejectAsync(id, request, cancellationToken);
        return TypedResults.Ok(summary);
    }
}
