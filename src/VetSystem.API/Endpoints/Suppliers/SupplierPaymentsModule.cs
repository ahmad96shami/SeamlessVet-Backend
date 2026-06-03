using VetSystem.API.Filters;
using VetSystem.API.Suppliers;
using VetSystem.Application.Purchasing.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Suppliers;

/// <summary>
/// M19 supplier payments (SCHEMA §4) — <c>/suppliers/{id}/payments</c>. Posting one reduces the
/// supplier balance. Writes require <see cref="PermissionKey.SuppliersWrite"/>.
/// </summary>
public sealed class SupplierPaymentsModule : IEndpointModule
{
    private const string EntityType = "supplier_payment";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/suppliers/{supplierId:guid}/payments")
            .RequireAuthorization()
            .WithTags("Suppliers");

        group.MapGet("/", List)
            .WithName("SupplierPayments_List");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.SuppliersWrite)
            .AddEndpointFilter<ValidationFilter<SupplierPaymentRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("SupplierPayments_Create");
    }

    private static async Task<IResult> List(
        Guid supplierId, SupplierPaymentsService svc, int? skip, int? take, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(supplierId, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Create(
        Guid supplierId,
        SupplierPaymentRequest request,
        SupplierPaymentsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.IssueAsync(supplierId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
