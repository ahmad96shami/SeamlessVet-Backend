using VetSystem.API.Contracts;
using VetSystem.API.Filters;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Contracts;

/// <summary>
/// Contract↔farm attach/detach (M15 task 7), nested under a contract. Authoring is gated on
/// <see cref="PermissionKey.ContractsWrite"/> (the field doctor authoring a draft); the service
/// additionally requires the parent contract to still be <c>draft</c> and the farm to belong to the
/// contract's own customer.
/// </summary>
public sealed class ContractFarmsModule : IEndpointModule
{
    private const string EntityType = "contract_farm";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/contracts/{contractId:guid}/farms")
            .RequireAuthorization()
            .WithTags("Contracts");

        group.MapGet("/", List).WithName("ContractFarms_List");

        group.MapPost("/", Attach)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter<ValidationFilter<ContractFarmAttachRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("ContractFarms_Attach");

        group.MapDelete("/{farmId:guid}", Detach)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("ContractFarms_Detach");
    }

    private static async Task<IResult> List(
        Guid contractId, ContractFarmsService svc, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(contractId, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Attach(
        Guid contractId,
        ContractFarmAttachRequest request,
        ContractFarmsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.AttachAsync(contractId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Detach(
        Guid contractId, Guid farmId, ContractFarmsService svc, CancellationToken cancellationToken)
    {
        await svc.DetachAsync(contractId, farmId, cancellationToken);
        return TypedResults.NoContent();
    }
}
