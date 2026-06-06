using VetSystem.API.Entitlements;
using VetSystem.API.Farms;
using VetSystem.API.Filters;
using VetSystem.Application.Farms.Contracts;
using VetSystem.Application.Ledgers;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Farms;

/// <summary>
/// Farm CRUD (M15). Idempotency-key required on mutations. Writes are gated on
/// <see cref="PermissionKey.CustomersWrite"/> — farms share the customer's permission scope (like
/// pets), since their lifecycle is tied to the customer record and a farm carries no doctor of its own.
/// </summary>
public sealed class FarmsModule : IEndpointModule
{
    private const string EntityType = "farm";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/farms")
            .RequireAuthorization()
            .WithTags("Farms");

        group.MapGet("/", List)
            .WithName("Farms_List");

        group.MapGet("/{id:guid}", Get)
            .WithName("Farms_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<FarmRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Farms_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter<ValidationFilter<FarmPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Farms_Update");

        group.MapDelete("/{id:guid}", Delete)
            .RequirePermission(PermissionKey.CustomersWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Farms_Delete");

        // M16 — the farm's ledger statement (mirrors the customer statement), ready for WhatsApp/print.
        group.MapGet("/{id:guid}/statement", Statement)
            .WithName("Farms_Statement");

        // M16 — close the farm's account (zero-balance only) and compute its entitlements. Payout
        // authority, so gated on entitlements.approve like the customer close.
        group.MapPost("/{id:guid}/close-account", CloseAccount)
            .RequirePermission(PermissionKey.EntitlementsApprove)
            .AddEndpointFilter(new IdempotencyKeyFilter("close_farm_account"))
            .WithName("Farms_CloseAccount");

        // M16 — re-open a settled (closed) farm ledger so its new visits can be billed (mirror of close).
        group.MapPost("/{id:guid}/reopen-account", ReopenAccount)
            .RequirePermission(PermissionKey.EntitlementsApprove)
            .AddEndpointFilter(new IdempotencyKeyFilter("reopen_farm_account"))
            .WithName("Farms_ReopenAccount");
    }

    private static async Task<IResult> List(
        FarmsService svc,
        Guid? customerId,
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(customerId, search, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, FarmsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        FarmRequest request,
        FarmsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        FarmPatchRequest request,
        FarmsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Delete(Guid id, FarmsService svc, CancellationToken cancellationToken)
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
        var statement = await ledgers.GetFarmStatementAsync(id, from, to, cancellationToken);
        return TypedResults.Ok(statement);
    }

    private static async Task<IResult> CloseAccount(
        Guid id,
        EntitlementSettlementService settlement,
        CancellationToken cancellationToken)
    {
        var result = await settlement.CloseFarmAccountAsync(id, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ReopenAccount(
        Guid id,
        EntitlementSettlementService settlement,
        CancellationToken cancellationToken)
    {
        var result = await settlement.ReopenFarmAccountAsync(id, cancellationToken);
        return TypedResults.Ok(result);
    }
}
