using VetSystem.API.Employees;
using VetSystem.API.Filters;
using VetSystem.Application.Employees.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Employees;

/// <summary>
/// M31 employee payments (SCHEMA §4) — <c>/employees/{id}/payments</c>. Posting one moves the employee's
/// balance (salary/loan debits, repayment credits). Writes require <see cref="PermissionKey.EmployeesPay"/>.
/// </summary>
public sealed class EmployeePaymentsModule : IEndpointModule
{
    private const string EntityType = "employee_payment";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/employees/{employeeId:guid}/payments")
            .RequireAuthorization()
            .WithTags("Employees");

        group.MapGet("/", List)
            .WithName("EmployeePayments_List");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.EmployeesPay)
            .AddEndpointFilter<ValidationFilter<EmployeePaymentRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("EmployeePayments_Create");
    }

    private static async Task<IResult> List(
        Guid employeeId, EmployeePaymentsService svc, int? skip, int? take, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(employeeId, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Create(
        Guid employeeId,
        EmployeePaymentRequest request,
        EmployeePaymentsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.IssueAsync(employeeId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
