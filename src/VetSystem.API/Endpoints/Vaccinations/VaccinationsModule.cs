using VetSystem.API.Filters;
using VetSystem.API.Vaccinations;
using VetSystem.Application.Vaccinations.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Vaccinations;

/// <summary>
/// Vaccination CRUD (PRD §5.2, §6.7, M5 task 12). Writes require
/// <see cref="PermissionKey.MedicalWrite"/> + an idempotency key; list is scoped with
/// <c>?petId=</c> / <c>?customerId=</c> / <c>?visitId=</c>.
/// </summary>
public sealed class VaccinationsModule : IEndpointModule
{
    private const string EntityType = "vaccination";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/vaccinations")
            .RequireAuthorization()
            .WithTags("Vaccinations");

        group.MapGet("/", List).WithName("Vaccinations_List");
        group.MapGet("/{id:guid}", Get).WithName("Vaccinations_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<VaccinationCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Vaccinations_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter<ValidationFilter<VaccinationPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Vaccinations_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.MedicalWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Vaccinations_Delete");
    }

    private static async Task<IResult> List(
        VaccinationsService svc, Guid? petId, Guid? customerId, Guid? visitId,
        int? skip, int? take, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.ListAsync(petId, customerId, visitId, skip, take, cancellationToken));

    private static async Task<IResult> Get(Guid id, VaccinationsService svc, CancellationToken cancellationToken)
        => TypedResults.Ok(await svc.GetAsync(id, cancellationToken));

    private static async Task<IResult> Create(
        VaccinationCreateRequest request, VaccinationsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, VaccinationPatchRequest request, VaccinationsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, VaccinationsService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
