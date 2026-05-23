using VetSystem.API.Contracts;
using VetSystem.API.Filters;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Contracts;

/// <summary>
/// Batch (Dawra/Cycle) CRUD (PRD §7.2, M8 task 8). Reads need only authentication; writes are gated
/// on <see cref="PermissionKey.ContractsActivate"/> — batch financial configuration is an
/// Admin/Accountant, online operation (PRD §7, §8.9), the same financial-confirm privilege that
/// activates contracts. The <c>/sync/batches</c> path is server-authoritative (read-only on device).
/// </summary>
public sealed class BatchesModule : IEndpointModule
{
    private const string EntityType = "batch";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/batches")
            .RequireAuthorization()
            .WithTags("Batches");

        group.MapGet("/", List).WithName("Batches_List");
        group.MapGet("/{id:guid}", Get).WithName("Batches_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.ContractsActivate)
            .AddEndpointFilter<ValidationFilter<BatchCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Batches_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.ContractsActivate)
            .AddEndpointFilter<ValidationFilter<BatchPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Batches_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.ContractsActivate)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Batches_Delete");
    }

    private static async Task<IResult> List(
        BatchesService svc,
        Guid? customerId,
        Guid? responsibleDoctorId,
        Guid? contractId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, responsibleDoctorId, contractId, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, BatchesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        BatchCreateRequest request, BatchesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, BatchPatchRequest request, BatchesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, BatchesService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
