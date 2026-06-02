using VetSystem.API.Filters;
using VetSystem.API.NightStays;
using VetSystem.Application.NightStays.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.NightStays;

/// <summary>
/// Night-stay (مبيت, PRD §18.6, M17) CRUD + close — clinic-only (the service rejects field visits).
/// Writes require <see cref="PermissionKey.MedicalWrite"/> + an idempotency key; list is scoped with
/// <c>?visitId=</c>. Closing a stay (<c>POST /{id}/close</c>) is what posts the boarding charge.
/// </summary>
public sealed class NightStaysModule : IEndpointModule
{
    private const string EntityType = "night_stay";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/night-stays")
            .RequireAuthorization()
            .WithTags("NightStays");

        group.MapGet("/", List).WithName("NightStays_List");
        group.MapGet("/{id:guid}", Get).WithName("NightStays_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<NightStayCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("NightStays_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<NightStayPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("NightStays_Update");

        group.MapPost("/{id:guid}/close", Close)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("NightStays_Close");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("NightStays_Delete");
    }

    private static async Task<IResult> List(
        NightStaysService svc, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(visitId, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, NightStaysService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(
        NightStayCreateRequest request, NightStaysService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.CreateAsync(request, cancellationToken));

    private static async Task<IResult> Update(
        Guid id, NightStayPatchRequest request, NightStaysService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.UpdateAsync(id, request, cancellationToken));

    private static async Task<IResult> Close(
        Guid id, NightStayCloseRequest? request, NightStaysService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.CloseAsync(id, request ?? new NightStayCloseRequest(null), cancellationToken));

    private static async Task<IResult> Delete(Guid id, NightStaysService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
