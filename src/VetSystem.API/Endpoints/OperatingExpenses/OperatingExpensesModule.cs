using VetSystem.API.Filters;
using VetSystem.API.OperatingExpenses;
using VetSystem.Application.OperatingExpenses.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.OperatingExpenses;

/// <summary>
/// Operating-expenses CRUD (water, electricity, rent, …). Reads scope via the global EF query filter;
/// writes require <see cref="PermissionKey.OperatingExpensesManage"/>. Online-only center-web.
/// </summary>
public sealed class OperatingExpensesModule : IEndpointModule
{
    private const string EntityType = "operating_expense";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/operating-expenses")
            .RequireAuthorization()
            .WithTags("OperatingExpenses");

        group.MapGet("/", List)
            .RequirePermission(PermissionKey.OperatingExpensesManage)
            .WithName("OperatingExpenses_List");

        group.MapGet("/{id:guid}", Get)
            .RequirePermission(PermissionKey.OperatingExpensesManage)
            .WithName("OperatingExpenses_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.OperatingExpensesManage)
            .AddEndpointFilter<ValidationFilter<CreateOperatingExpenseRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("OperatingExpenses_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.OperatingExpensesManage)
            .AddEndpointFilter<ValidationFilter<UpdateOperatingExpenseRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("OperatingExpenses_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.OperatingExpensesManage)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("OperatingExpenses_Delete");
    }

    private static async Task<IResult> List(
        OperatingExpensesService svc,
        string? category,
        DateOnly? from,
        DateOnly? to,
        bool? paid,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(category, from, to, paid, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, OperatingExpensesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        CreateOperatingExpenseRequest request, OperatingExpensesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Update(
        Guid id, UpdateOperatingExpenseRequest request, OperatingExpensesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Delete(Guid id, OperatingExpensesService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
