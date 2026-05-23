using VetSystem.API.Filters;
using VetSystem.API.Financial;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Financial;

/// <summary>
/// Invoice endpoints (M7). Reads need only authentication; issuance is gated on
/// <see cref="PermissionKey.InvoicesWrite"/> and carries an idempotency key. POS issuance is its own
/// route (<c>/pos/invoices</c>); field and exam-fee issuance are visit-scoped and added alongside.
/// </summary>
public sealed class InvoicesModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var invoices = endpoints.MapGroup("/invoices")
            .RequireAuthorization()
            .WithTags("Invoices");

        invoices.MapGet("/", List).WithName("Invoices_List");
        invoices.MapGet("/{id:guid}", Get).WithName("Invoices_Get");

        invoices.MapPost("/{id:guid}/void", Void)
            .RequirePermission(PermissionKey.InvoicesVoid)
            .AddEndpointFilter(new IdempotencyKeyFilter("invoice_void"))
            .WithName("Invoices_Void");

        var pos = endpoints.MapGroup("/pos/invoices")
            .RequireAuthorization()
            .WithTags("Invoices");

        pos.MapPost("/", CreatePos)
            .RequirePermission(PermissionKey.InvoicesWrite)
            .AddEndpointFilter<ValidationFilter<PosInvoiceRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("pos_invoice"))
            .WithName("Invoices_CreatePos");

        // Visit-scoped issuance: field invoice (deducts from the doctor's field inventory) and the
        // standalone exam-fee invoice (System B input for M9).
        var visitInvoices = endpoints.MapGroup("/visits/{visitId:guid}")
            .RequireAuthorization()
            .WithTags("Invoices");

        visitInvoices.MapPost("/field-invoice", CreateField)
            .RequirePermission(PermissionKey.InvoicesWrite)
            .AddEndpointFilter<ValidationFilter<FieldInvoiceRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("field_invoice"))
            .WithName("Invoices_CreateField");

        visitInvoices.MapPost("/exam-fee-invoice", CreateExamFee)
            .RequirePermission(PermissionKey.InvoicesWrite)
            .AddEndpointFilter<ValidationFilter<ExamFeeInvoiceRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("exam_fee_invoice"))
            .WithName("Invoices_CreateExamFee");
    }

    private static async Task<IResult> List(
        InvoicesService svc,
        Guid? customerId,
        Guid? visitId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, visitId, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, InvoicesService svc, CancellationToken cancellationToken)
    {
        var invoice = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(invoice);
    }

    private static async Task<IResult> Void(Guid id, InvoicesService svc, CancellationToken cancellationToken)
    {
        var voidInvoice = await svc.VoidAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(voidInvoice.Id));
    }

    private static async Task<IResult> CreatePos(
        PosInvoiceRequest request,
        InvoicesService svc,
        CancellationToken cancellationToken)
    {
        var invoice = await svc.IssuePosAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(invoice.Id));
    }

    private static async Task<IResult> CreateField(
        Guid visitId,
        FieldInvoiceRequest request,
        InvoicesService svc,
        CancellationToken cancellationToken)
    {
        var invoice = await svc.IssueFieldAsync(visitId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(invoice.Id));
    }

    private static async Task<IResult> CreateExamFee(
        Guid visitId,
        ExamFeeInvoiceRequest request,
        InvoicesService svc,
        CancellationToken cancellationToken)
    {
        var invoice = await svc.IssueExamFeeAsync(visitId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(invoice.Id));
    }
}
