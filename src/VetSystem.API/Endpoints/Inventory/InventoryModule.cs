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

        // Reads (authenticated; BACKEND_PREREQS §2) — the web reads on-hand + history over REST.
        group.MapGet("/stock", ListStock)
            .WithName("Inventory_Stock")
            .WithSummary("Current on-hand per product per location (stock list + alert views).");

        group.MapGet("/movements", ListMovements)
            .WithName("Inventory_Movements")
            .WithSummary("Append-only stock movement history, filterable + offset-paged.");

        group.MapGet("/field-inventories", ListFieldInventories)
            .WithName("Inventory_FieldInventories")
            .WithSummary("Field doctors' inventories — the load/unload doctor picker.");

        group.MapGet("/lots", ListLots)
            .WithName("Inventory_Lots")
            .WithSummary("FEFO lots of a product (cost + expiry + remaining), earliest-expiry first.");

        group.MapGet("/expiring", ListExpiring)
            .WithName("Inventory_Expiring")
            .WithSummary("On-hand lots near expiry (lot-accurate near-expiry alert view).");

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

        group.MapPost("/consume", Consume)
            .RequirePermission(PermissionKey.InventoryAdjust)
            .AddEndpointFilter<ValidationFilter<ConsumeStockRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter("inventory_consume"))
            .WithName("Inventory_Consume")
            .WithSummary("Record internal-use consumption of a consumable (FEFO-deducts lots).");
    }

    private static async Task<IResult> ListStock(
        InventoryReadService svc,
        string? locationType,
        Guid? locationId,
        Guid? productId,
        string? search,
        bool? lowStockOnly,
        bool? includeZeroStock,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListStockAsync(
            locationType, locationId, productId, search, lowStockOnly, includeZeroStock, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> ListMovements(
        InventoryReadService svc,
        Guid? productId,
        string? locationType,
        Guid? locationId,
        string? movementType,
        DateOnly? from,
        DateOnly? to,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListMovementsAsync(
            productId, locationType, locationId, movementType, from, to, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> ListFieldInventories(
        InventoryReadService svc,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListFieldInventoriesAsync(cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> ListLots(
        InventoryReadService svc,
        Guid productId,
        string? locationType,
        Guid? locationId,
        bool? onHandOnly,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListLotsAsync(productId, locationType, locationId, onHandOnly, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> ListExpiring(
        InventoryReadService svc,
        int? withinDays,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListExpiringAsync(withinDays, cancellationToken);
        return TypedResults.Ok(items);
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

    private static async Task<IResult> Consume(
        ConsumeStockRequest request,
        InventoryAdminService svc,
        CancellationToken cancellationToken)
    {
        var result = await svc.ConsumeAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.MovementId));
    }
}
