using VetSystem.API.Entitlements;
using VetSystem.API.Filters;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Entitlements;

/// <summary>
/// Doctor-entitlement reads + settlement transitions (M9 tasks 10–11). Reads need only authentication
/// (a doctor sees their own accruals via the env scope); approve/pay are payout authority gated on
/// <see cref="PermissionKey.EntitlementsApprove"/> and carry an idempotency key. Computation is not a
/// client action — entitlements are produced server-side by the close-account workflow; the
/// <c>/sync/doctor_entitlements</c> path is read-only.
/// </summary>
public sealed class EntitlementsModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/doctor-entitlements")
            .RequireAuthorization()
            .WithTags("Doctor Entitlements");

        group.MapGet("/", List).WithName("Entitlements_List");
        group.MapGet("/{id:guid}", Get).WithName("Entitlements_Get");

        group.MapPost("/{id:guid}/approve", Approve)
            .RequirePermission(PermissionKey.EntitlementsApprove)
            .AddEndpointFilter(new IdempotencyKeyFilter("entitlement_approve"))
            .WithName("Entitlements_Approve");

        group.MapPost("/{id:guid}/pay", Pay)
            .RequirePermission(PermissionKey.EntitlementsApprove)
            .AddEndpointFilter<ValidationFilter<PayEntitlementRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("entitlement_pay"))
            .WithName("Entitlements_Pay");
    }

    private static async Task<IResult> List(
        EntitlementSettlementService svc,
        Guid? doctorId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(doctorId, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, EntitlementSettlementService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Approve(Guid id, EntitlementSettlementService svc, CancellationToken cancellationToken)
    {
        var item = await svc.ApproveAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Pay(
        Guid id, PayEntitlementRequest request, EntitlementSettlementService svc, CancellationToken cancellationToken)
    {
        var item = await svc.PayAsync(id, request, cancellationToken);
        return TypedResults.Ok(item);
    }
}
