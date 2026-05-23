using VetSystem.API.Filters;
using VetSystem.API.Procedures;
using VetSystem.Application.Procedures.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Procedures;

/// <summary>
/// Procedure CRUD (PRD §5.2-C, M5 task 7). Writes require <see cref="PermissionKey.MedicalWrite"/>
/// + an idempotency key; reads need only authentication. List is scoped with <c>?visitId=</c>.
/// </summary>
public sealed class ProceduresModule : IEndpointModule
{
    private const string EntityType = "procedure";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/procedures")
            .RequireAuthorization()
            .WithTags("Procedures");

        group.MapGet("/", List).WithName("Procedures_List");
        group.MapGet("/{id:guid}", Get).WithName("Procedures_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<ProcedureCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Procedures_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<ProcedurePatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Procedures_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Procedures_Delete");
    }

    private static async Task<IResult> List(
        ProceduresService svc, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(visitId, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, ProceduresService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(
        ProcedureCreateRequest request, ProceduresService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, ProcedurePatchRequest request, ProceduresService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, ProceduresService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
