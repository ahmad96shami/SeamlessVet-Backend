using VetSystem.API.Entitlements;

namespace VetSystem.API.Endpoints.Entitlements;

/// <summary>
/// Doctor-entitlement reads (M9; M30 — read-only). A doctor sees their own batch accruals via the env
/// scope. Entitlements are computed and credited to the doctor-partner ledger when a batch is settled
/// (تصفية); the approve/pay lifecycle and the settlement lock were removed in M30. The
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
    }

    private static async Task<IResult> List(
        EntitlementSettlementService svc,
        Guid? doctorId,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(doctorId, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, EntitlementSettlementService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }
}
