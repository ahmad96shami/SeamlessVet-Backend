using VetSystem.API.Filters;
using VetSystem.API.Suppliers;
using VetSystem.Application.SupplierLedgers;
using VetSystem.Application.Suppliers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Suppliers;

/// <summary>
/// M19 supplier CRUD + statement (SCHEMA §4). Online-only center-web. Reads scope via the global EF
/// query filter; writes require <see cref="PermissionKey.SuppliersWrite"/>.
/// </summary>
public sealed class SuppliersModule : IEndpointModule
{
    private const string EntityType = "supplier";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/suppliers")
            .RequireAuthorization()
            .WithTags("Suppliers");

        group.MapGet("/", List)
            .WithName("Suppliers_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Suppliers_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.SuppliersWrite)
            .AddEndpointFilter<ValidationFilter<SupplierRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Suppliers_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.SuppliersWrite)
            .AddEndpointFilter<ValidationFilter<SupplierPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Suppliers_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.SuppliersWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Suppliers_Delete");

        group.MapGet("/{id:guid}/statement", Statement)
            .WithName("Suppliers_Statement");
    }

    private static async Task<IResult> List(
        SuppliersService svc,
        string? search,
        string? ledgerStatus,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(search, ledgerStatus, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, SuppliersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        SupplierRequest request, SuppliersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, SupplierPatchRequest request, SuppliersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, SuppliersService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Statement(
        Guid id,
        ISupplierLedgerService ledgers,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var statement = await ledgers.GetStatementAsync(id, from, to, cancellationToken);
        return TypedResults.Ok(statement);
    }
}
