using VetSystem.API.DailyFollowUps;
using VetSystem.API.Filters;
using VetSystem.Application.DailyFollowUps.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.DailyFollowUps;

/// <summary>
/// Daily follow-up CRUD (PRD §5.2-E, M5 task 11) — clinic-only (the service rejects field visits).
/// Writes require <see cref="PermissionKey.MedicalWrite"/> + an idempotency key; list is scoped
/// with <c>?visitId=</c>.
/// </summary>
public sealed class DailyFollowUpsModule : IEndpointModule
{
    private const string EntityType = "daily_follow_up";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/daily-follow-ups")
            .RequireAuthorization()
            .WithTags("DailyFollowUps");

        group.MapGet("/", List).WithName("DailyFollowUps_List");
        group.MapGet("/{id:guid}", Get).WithName("DailyFollowUps_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<DailyFollowUpCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DailyFollowUps_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<DailyFollowUpPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DailyFollowUps_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DailyFollowUps_Delete");
    }

    private static async Task<IResult> List(
        DailyFollowUpsService svc, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(visitId, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, DailyFollowUpsService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(
        DailyFollowUpCreateRequest request, DailyFollowUpsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, DailyFollowUpPatchRequest request, DailyFollowUpsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, DailyFollowUpsService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
