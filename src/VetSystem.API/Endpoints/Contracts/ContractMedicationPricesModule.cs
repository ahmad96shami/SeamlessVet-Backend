using VetSystem.API.Contracts;
using VetSystem.API.Filters;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Contracts;

/// <summary>
/// Per-medication contract price CRUD (PRD §6.6, M8 task 7), nested under a contract. Authoring is
/// gated on <see cref="PermissionKey.ContractsWrite"/> (the field doctor authoring a draft); the
/// service additionally requires the parent contract to still be <c>draft</c>.
/// </summary>
public sealed class ContractMedicationPricesModule : IEndpointModule
{
    private const string EntityType = "contract_medication_price";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/contracts/{contractId:guid}/medication-prices")
            .RequireAuthorization()
            .WithTags("Contracts");

        group.MapGet("/", List).WithName("ContractMedicationPrices_List");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter<ValidationFilter<ContractMedicationPriceCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("ContractMedicationPrices_Create");

        group.MapPatch("/{priceId:guid}", Update)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter<ValidationFilter<ContractMedicationPricePatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("ContractMedicationPrices_Update");

        group.MapDelete("/{priceId:guid}", Delete)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("ContractMedicationPrices_Delete");
    }

    private static async Task<IResult> List(
        Guid contractId, ContractMedicationPricesService svc, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(contractId, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Create(
        Guid contractId,
        ContractMedicationPriceCreateRequest request,
        ContractMedicationPricesService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(contractId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid contractId,
        Guid priceId,
        ContractMedicationPricePatchRequest request,
        ContractMedicationPricesService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(contractId, priceId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(
        Guid contractId, Guid priceId, ContractMedicationPricesService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(contractId, priceId, cancellationToken);
        return TypedResults.NoContent();
    }
}
