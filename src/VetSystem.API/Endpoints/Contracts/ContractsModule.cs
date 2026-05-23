using VetSystem.API.Contracts;
using VetSystem.API.Filters;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Contracts;

/// <summary>
/// Contract lifecycle endpoints (PRD §6.6, M8 tasks 2–6). Reads need only authentication. Authoring
/// (create/edit/cancel) is gated on <see cref="PermissionKey.ContractsWrite"/>; the binding
/// transitions — <c>/activate</c> and <c>/complete</c> — are gated on
/// <see cref="PermissionKey.ContractsActivate"/> (the activation gate). Being dedicated online
/// endpoints is what makes activation "server-confirmed" (PRD §8.9); the <c>/sync</c> path refuses
/// the <c>draft → active</c> edge.
/// </summary>
public sealed class ContractsModule : IEndpointModule
{
    private const string EntityType = "contract";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/contracts")
            .RequireAuthorization()
            .WithTags("Contracts");

        group.MapGet("/", List).WithName("Contracts_List");
        group.MapGet("/{id:guid}", Get).WithName("Contracts_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter<ValidationFilter<ContractCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Contracts_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter<ValidationFilter<ContractPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Contracts_Update");

        group.MapPost("/{id:guid}/activate", Activate)
            .RequirePermission(PermissionKey.ContractsActivate)
            .AddEndpointFilter(new IdempotencyKeyFilter("contract_activate"))
            .WithName("Contracts_Activate");

        group.MapPost("/{id:guid}/complete", Complete)
            .RequirePermission(PermissionKey.ContractsActivate)
            .AddEndpointFilter(new IdempotencyKeyFilter("contract_complete"))
            .WithName("Contracts_Complete");

        group.MapPost("/{id:guid}/cancel", Cancel)
            .RequirePermission(PermissionKey.ContractsWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter("contract_cancel"))
            .WithName("Contracts_Cancel");
    }

    private static async Task<IResult> List(
        ContractsService svc,
        Guid? customerId,
        Guid? responsibleDoctorId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, responsibleDoctorId, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, ContractsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        ContractCreateRequest request, ContractsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, ContractPatchRequest request, ContractsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Activate(Guid id, ContractsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.ActivateAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Complete(Guid id, ContractsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CompleteAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Cancel(Guid id, ContractsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CancelAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }
}
