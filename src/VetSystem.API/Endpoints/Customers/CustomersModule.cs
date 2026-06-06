using VetSystem.API.Customers;
using VetSystem.API.Entitlements;
using VetSystem.API.Filters;
using VetSystem.Application.Customers.Contracts;
using VetSystem.Application.Ledgers;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Customers;

/// <summary>
/// Operational customer CRUD (PRD §5.1, M3). Idempotency-key required on mutations.
/// Reads scope via the global EF query filter; write operations require
/// <see cref="PermissionKey.CustomersWrite"/>.
/// </summary>
public sealed class CustomersModule : IEndpointModule
{
    private const string EntityType = "customer";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/customers")
            .RequireAuthorization()
            .WithTags("Customers");

        group.MapGet("/", List)
            .WithName("Customers_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Customers_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<CustomerRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Customers_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<CustomerPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Customers_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Customers_Delete");

        // M3 task 8 — full ledger statement, ready for WhatsApp/email/print on the client.
        group.MapGet("/{id:guid}/statement", Statement)
            .WithName("Customers_Statement");

        // M9 task 9 — close the account (zero-balance only) and trigger the entitlement settlement
        // workflow (PRD §7.7). Payout authority, so gated on entitlements.approve.
        group.MapPost("/{id:guid}/close-account", CloseAccount)
            .RequirePermission(PermissionKey.EntitlementsApprove)
            .AddEndpointFilter(new IdempotencyKeyFilter("close_account"))
            .WithName("Customers_CloseAccount");

        // Re-open a settled (closed) own ledger so a returning customer's new visit can be billed.
        // Same settlement authority as close; idempotent.
        group.MapPost("/{id:guid}/reopen-account", ReopenAccount)
            .RequirePermission(PermissionKey.EntitlementsApprove)
            .AddEndpointFilter(new IdempotencyKeyFilter("reopen_account"))
            .WithName("Customers_ReopenAccount");
    }

    private static async Task<IResult> List(
        CustomersService svc,
        string? search,
        string? type,
        Guid? assignedDoctorId,
        string? ledgerStatus,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(search, type, assignedDoctorId, ledgerStatus, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, CustomersService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        CustomerRequest request,
        CustomersService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        CustomerPatchRequest request,
        CustomersService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, CustomersService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Statement(
        Guid id,
        ILedgerService ledgers,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var statement = await ledgers.GetStatementAsync(id, from, to, cancellationToken);
        return TypedResults.Ok(statement);
    }

    private static async Task<IResult> CloseAccount(
        Guid id,
        EntitlementSettlementService settlement,
        CancellationToken cancellationToken)
    {
        var result = await settlement.CloseCustomerAccountAsync(id, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ReopenAccount(
        Guid id,
        EntitlementSettlementService settlement,
        CancellationToken cancellationToken)
    {
        var result = await settlement.ReopenCustomerAccountAsync(id, cancellationToken);
        return TypedResults.Ok(result);
    }
}
