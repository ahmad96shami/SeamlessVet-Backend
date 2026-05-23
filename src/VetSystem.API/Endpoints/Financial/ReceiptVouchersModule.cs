using VetSystem.API.Filters;
using VetSystem.API.Financial;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Financial;

/// <summary>
/// Receipt-voucher (Sanad Qabd) endpoints (M7 task 9). Issuance is gated on
/// <see cref="PermissionKey.InvoicesWrite"/> and idempotent; it posts the customer ledger credit.
/// </summary>
public sealed class ReceiptVouchersModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/receipt-vouchers")
            .RequireAuthorization()
            .WithTags("ReceiptVouchers");

        group.MapGet("/", List).WithName("ReceiptVouchers_List");
        group.MapGet("/{id:guid}", Get).WithName("ReceiptVouchers_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.InvoicesWrite)
            .AddEndpointFilter<ValidationFilter<ReceiptVoucherRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("receipt_voucher"))
            .WithName("ReceiptVouchers_Create");
    }

    private static async Task<IResult> List(
        ReceiptVouchersService svc, Guid? customerId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, ReceiptVouchersService svc, CancellationToken cancellationToken)
    {
        var voucher = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(voucher);
    }

    private static async Task<IResult> Create(
        ReceiptVoucherRequest request, ReceiptVouchersService svc, CancellationToken cancellationToken)
    {
        var voucher = await svc.IssueAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(voucher.Id));
    }
}
