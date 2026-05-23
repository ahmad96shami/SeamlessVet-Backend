using VetSystem.API.Filters;
using VetSystem.API.Prescriptions;
using VetSystem.Application.Prescriptions.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Prescriptions;

/// <summary>
/// Prescription CRUD (PRD §5.2-D, M5 tasks 8–10). Create branches on <c>dispense_type</c>:
/// <c>administered_in_clinic</c> deducts inventory atomically, <c>dispensed_to_owner</c> raises the
/// POS event. Writes require <see cref="PermissionKey.MedicalWrite"/> + an idempotency key (which
/// also makes the inventory deduction exactly-once on offline replay); reads need only auth.
/// </summary>
public sealed class PrescriptionsModule : IEndpointModule
{
    private const string EntityType = "prescription";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/prescriptions")
            .RequireAuthorization()
            .WithTags("Prescriptions");

        group.MapGet("/", List).WithName("Prescriptions_List");
        group.MapGet("/{id:guid}", Get).WithName("Prescriptions_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<PrescriptionCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Prescriptions_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<PrescriptionPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Prescriptions_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Prescriptions_Delete");
    }

    private static async Task<IResult> List(
        PrescriptionsService svc, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(visitId, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, PrescriptionsService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(
        PrescriptionCreateRequest request, PrescriptionsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, PrescriptionPatchRequest request, PrescriptionsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, PrescriptionsService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
