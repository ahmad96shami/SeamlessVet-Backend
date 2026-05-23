using VetSystem.API.Filters;
using VetSystem.API.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Inventory;

/// <summary>
/// Online-preferred inventory operations (PRD §8.9, M4 tasks 6–9). All four endpoints require the
/// <see cref="PermissionKey.InventoryAdjust"/> permission (Admin / Inventory staff) and an
/// idempotency key. Each translates to signed <c>inventory_movements</c> via
/// <see cref="InventoryAdminService"/>; clients never write absolute quantities.
/// </summary>
public sealed class InventoryModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/inventory")
            .RequireAuthorization()
            .WithTags("Inventory");

        group.MapPost("/receive", Receive)
            .RequirePermission(PermissionKey.InventoryAdjust)
            .AddEndpointFilter<ValidationFilter<ReceiveStockRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("inventory_receive"))
            .WithName("Inventory_Receive")
            .WithSummary("Receive stock into a warehouse (purchase order).");

        group.MapPost("/adjust", Adjust)
            .RequirePermission(PermissionKey.InventoryAdjust)
            .AddEndpointFilter<ValidationFilter<AdjustStockRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("inventory_adjust"))
            .WithName("Inventory_Adjust")
            .WithSummary("Apply a signed stock adjustment at a location (with reason).");

        group.MapPost("/load-field", LoadField)
            .RequirePermission(PermissionKey.InventoryAdjust)
            .AddEndpointFilter<ValidationFilter<LoadFieldRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("inventory_load_field"))
            .WithName("Inventory_LoadField")
            .WithSummary("Transfer stock from the central warehouse to a field inventory.");

        group.MapPost("/unload-field", UnloadField)
            .RequirePermission(PermissionKey.InventoryAdjust)
            .AddEndpointFilter<ValidationFilter<UnloadFieldRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("inventory_unload_field"))
            .WithName("Inventory_UnloadField")
            .WithSummary("Return stock from a field inventory to the central warehouse.");
    }

    private static async Task<IResult> Receive(
        ReceiveStockRequest request,
        InventoryAdminService svc,
        CancellationToken cancellationToken)
    {
        var result = await svc.ReceiveAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.MovementId));
    }

    private static async Task<IResult> Adjust(
        AdjustStockRequest request,
        InventoryAdminService svc,
        CancellationToken cancellationToken)
    {
        var result = await svc.AdjustAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.MovementId));
    }

    private static async Task<IResult> LoadField(
        LoadFieldRequest request,
        InventoryAdminService svc,
        CancellationToken cancellationToken)
    {
        var result = await svc.LoadFieldAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.MovementId));
    }

    private static async Task<IResult> UnloadField(
        UnloadFieldRequest request,
        InventoryAdminService svc,
        CancellationToken cancellationToken)
    {
        var result = await svc.UnloadFieldAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.MovementId));
    }
}
