using VetSystem.API.DoctorPartners;
using VetSystem.API.Filters;
using VetSystem.Application.DoctorPartnerLedgers;
using VetSystem.Application.DoctorPartners.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.DoctorPartners;

/// <summary>
/// M30 doctor-partner CRUD + statement (SCHEMA §4). Online-only center-web. Reads scope via the global
/// EF query filter; writes require <see cref="PermissionKey.DoctorPartnersManage"/>.
/// </summary>
public sealed class DoctorPartnersModule : IEndpointModule
{
    private const string EntityType = "doctor_partner";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/doctor-partners")
            .RequireAuthorization()
            .WithTags("Doctor Partners");

        group.MapGet("/", List)
            .WithName("DoctorPartners_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("DoctorPartners_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.DoctorPartnersManage)
            .AddEndpointFilter<ValidationFilter<DoctorPartnerRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DoctorPartners_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.DoctorPartnersManage)
            .AddEndpointFilter<ValidationFilter<DoctorPartnerPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DoctorPartners_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.DoctorPartnersManage)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DoctorPartners_Delete");

        group.MapGet("/{id:guid}/statement", Statement)
            .WithName("DoctorPartners_Statement");
    }

    private static async Task<IResult> List(
        DoctorPartnersService svc,
        string? search,
        string? ledgerStatus,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(search, ledgerStatus, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, DoctorPartnersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        DoctorPartnerRequest request, DoctorPartnersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, DoctorPartnerPatchRequest request, DoctorPartnersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, DoctorPartnersService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Statement(
        Guid id,
        IDoctorPartnerLedgerService ledgers,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var statement = await ledgers.GetStatementAsync(id, from, to, cancellationToken);
        return TypedResults.Ok(statement);
    }
}
