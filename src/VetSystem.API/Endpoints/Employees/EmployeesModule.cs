using VetSystem.API.Employees;
using VetSystem.API.Filters;
using VetSystem.Application.EmployeeLedgers;
using VetSystem.Application.Employees.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Employees;

/// <summary>
/// M31 employee CRUD + statement (SCHEMA §4). Online-only center-web. Reads scope via the global EF query
/// filter; writes require <see cref="PermissionKey.EmployeesManage"/>.
/// </summary>
public sealed class EmployeesModule : IEndpointModule
{
    private const string EntityType = "employee";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/employees")
            .RequireAuthorization()
            .WithTags("Employees");

        group.MapGet("/", List)
            .WithName("Employees_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Employees_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.EmployeesManage)
            .AddEndpointFilter<ValidationFilter<EmployeeRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Employees_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.EmployeesManage)
            .AddEndpointFilter<ValidationFilter<EmployeePatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Employees_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.EmployeesManage)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Employees_Delete");

        group.MapGet("/{id:guid}/statement", Statement)
            .WithName("Employees_Statement");
    }

    private static async Task<IResult> List(
        EmployeesService svc,
        string? search,
        string? ledgerStatus,
        bool? active,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(search, ledgerStatus, active, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, EmployeesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        EmployeeRequest request, EmployeesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id, EmployeePatchRequest request, EmployeesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, EmployeesService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Statement(
        Guid id,
        IEmployeeLedgerService ledgers,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var statement = await ledgers.GetStatementAsync(id, from, to, cancellationToken);
        return TypedResults.Ok(statement);
    }
}
