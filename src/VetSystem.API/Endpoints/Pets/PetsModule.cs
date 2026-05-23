using VetSystem.API.Filters;
using VetSystem.API.Pets;
using VetSystem.Application.Pets.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Pets;

/// <summary>
/// Pet CRUD + ownership-transfer endpoint (PRD §5.1, M3). Idempotency-key required on
/// mutations. Writes are gated on <see cref="PermissionKey.CustomersWrite"/> — pets share
/// the customer's permission scope since their lifecycle is tied to the customer record.
/// </summary>
public sealed class PetsModule : IEndpointModule
{
    private const string EntityType = "pet";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/pets")
            .RequireAuthorization()
            .WithTags("Pets");

        group.MapGet("/", List)
            .WithName("Pets_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Pets_Get");

        // M5 task 17 — chronological medical timeline (clinic + field visits) for a pet.
        group.MapGet("/{id:guid}/timeline", Timeline)
            .WithName("Pets_Timeline");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<PetRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Pets_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<PetPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Pets_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Pets_Delete");

        // M3 task 4 — ownership transfer is its own endpoint, not a free-form PATCH.
        group.MapPost("/{id:guid}/transfer", Transfer)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<PetTransferRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("pet_transfer"))
            .WithName("Pets_Transfer");
    }

    private static async Task<IResult> List(
        PetsService svc,
        Guid? customerId,
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, search, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, PetsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Timeline(
        Guid id,
        PetTimelineService svc,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? doctorId,
        CancellationToken cancellationToken)
    {
        var timeline = await svc.GetAsync(id, from, to, doctorId, cancellationToken);
        return TypedResults.Ok(timeline);
    }

    private static async Task<IResult> Create(
        PetRequest request,
        PetsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        PetPatchRequest request,
        PetsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, PetsService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Transfer(
        Guid id,
        PetTransferRequest request,
        PetsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.TransferAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
