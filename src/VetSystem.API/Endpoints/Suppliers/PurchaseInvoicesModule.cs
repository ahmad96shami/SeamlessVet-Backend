using VetSystem.API.Filters;
using VetSystem.API.Suppliers;
using VetSystem.Application.Purchasing.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Suppliers;

/// <summary>
/// M19 purchase invoices (SCHEMA §4). Issuing one receives goods into the warehouse and posts the
/// supplier payable in a single transaction. Writes require <see cref="PermissionKey.SuppliersWrite"/>.
/// </summary>
public sealed class PurchaseInvoicesModule : IEndpointModule
{
    private const string EntityType = "purchase_invoice";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/purchase-invoices")
            .RequireAuthorization()
            .WithTags("Purchase Invoices");

        group.MapGet("/", List)
            .WithName("PurchaseInvoices_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("PurchaseInvoices_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.SuppliersWrite)
            .AddEndpointFilter<ValidationFilter<PurchaseInvoiceRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("PurchaseInvoices_Create");
    }

    private static async Task<IResult> List(
        PurchaseInvoicesService svc, Guid? supplierId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(supplierId, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, PurchaseInvoicesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        PurchaseInvoiceRequest request, PurchaseInvoicesService svc, CancellationToken cancellationToken)
    {
        var item = await svc.IssueAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
