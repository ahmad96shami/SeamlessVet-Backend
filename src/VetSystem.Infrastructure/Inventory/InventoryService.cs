using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Inventory;

/// <summary>
/// Delta-only, append-only implementation of <see cref="IInventoryService"/> (SCHEMA "Key
/// invariants" #2). <see cref="MovementTranslator"/> turns the intent into signed legs; each leg
/// writes one <c>inventory_movements</c> row and upserts the affected <c>stock_items</c> balance,
/// all in a single <c>SaveChanges</c> so the new movement(s), the recomputed quantity, and (for a
/// transfer) both legs commit together or not at all. The unique
/// <c>(environment_id, idempotency_key)</c> index converts retried offline writes into idempotent
/// replays. A leg that would push a balance below zero publishes
/// <see cref="NegativeStockAttemptedEvent"/> and then rejects the whole write.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private const string SecondLegSuffix = ":2";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMovementTranslator _translator;
    private readonly IDomainEventPublisher _events;

    public InventoryService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMovementTranslator translator,
        IDomainEventPublisher events)
    {
        _db = db;
        _currentUser = currentUser;
        _translator = translator;
        _events = events;
    }

    public async Task<MovementResult> ApplyMovementAsync(MovementIntent intent, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(intent.IdempotencyKey))
        {
            throw new ConflictException("idempotency_key_required",
                "inventory movements require a non-empty idempotency_key.");
        }

        // Replay: the whole operation commits atomically, so leg-0's key existing means it all applied.
        var alreadyApplied = await _db.InventoryMovements
            .AsNoTracking()
            .AnyAsync(m => m.EnvironmentId == envId && m.IdempotencyKey == intent.IdempotencyKey, cancellationToken);
        if (alreadyApplied)
        {
            return await BuildReplayResultAsync(envId, intent.IdempotencyKey, cancellationToken);
        }

        var legs = _translator.Translate(intent);

        await EnsureProductExistsAsync(intent.ProductId, cancellationToken);

        var movements = new List<InventoryMovement>(legs.Count);
        var balances = new List<StockBalance>(legs.Count);

        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var locationType = leg.AffectedLocationType;
            var locationId = leg.AffectedLocationId;

            await EnsureLocationExistsAsync(locationType, locationId, cancellationToken);

            var stock = await _db.StockItems.FirstOrDefaultAsync(
                s => s.LocationType == locationType
                     && s.LocationId == locationId
                     && s.ProductId == intent.ProductId,
                cancellationToken);

            var current = stock?.Quantity ?? 0m;
            var updated = current + leg.SignedDelta;

            if (updated < 0m)
            {
                // Publish before rejecting so M11 can notify; the throw rolls back (nothing persisted).
                await _events.PublishAsync(
                    new NegativeStockAttemptedEvent(
                        envId, intent.ProductId, locationType, locationId,
                        leg.SignedDelta, current, userId, intent.VisitId),
                    cancellationToken);

                throw new ConflictException("negative_stock",
                    $"Movement would drive {locationType} stock for product {intent.ProductId} negative "
                    + $"(current {current}, delta {leg.SignedDelta}).");
            }

            if (stock is null)
            {
                _db.StockItems.Add(new StockItem
                {
                    EnvironmentId = envId,
                    LocationType = locationType,
                    LocationId = locationId,
                    ProductId = intent.ProductId,
                    Quantity = updated,
                });
            }
            else
            {
                stock.Quantity = updated;
            }

            var movement = new InventoryMovement
            {
                Id = i == 0 ? intent.Id ?? Guid.Empty : Guid.Empty,
                EnvironmentId = envId,
                ProductId = intent.ProductId,
                MovementType = intent.MovementType,
                FromLocationType = leg.FromLocationType,
                FromLocationId = leg.FromLocationId,
                ToLocationType = leg.ToLocationType,
                ToLocationId = leg.ToLocationId,
                QuantityDelta = leg.SignedDelta,
                Reason = intent.Reason,
                VisitId = intent.VisitId,
                InvoiceId = intent.InvoiceId,
                PurchaseInvoiceId = intent.PurchaseInvoiceId,
                PerformedBy = userId,
                IdempotencyKey = i == 0 ? intent.IdempotencyKey : $"{intent.IdempotencyKey}{SecondLegSuffix}",
            };
            _db.InventoryMovements.Add(movement);

            movements.Add(movement);
            balances.Add(new StockBalance(locationType, locationId, intent.ProductId, updated));
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            // Concurrent retry of the same key won the race — return the row(s) that landed.
            return await BuildReplayResultAsync(envId, intent.IdempotencyKey, cancellationToken);
        }

        return new MovementResult(
            MovementId: movements[0].Id,
            MovementIds: movements.Select(m => m.Id).ToList(),
            Balances: balances,
            Replayed: false);
    }

    private async Task<MovementResult> BuildReplayResultAsync(Guid envId, string baseKey, CancellationToken cancellationToken)
    {
        var secondKey = baseKey + SecondLegSuffix;
        var rows = await _db.InventoryMovements
            .AsNoTracking()
            .Where(m => m.EnvironmentId == envId && (m.IdempotencyKey == baseKey || m.IdempotencyKey == secondKey))
            .ToListAsync(cancellationToken);

        var primary = rows.First(m => m.IdempotencyKey == baseKey);
        var ordered = rows.OrderBy(m => m.IdempotencyKey == baseKey ? 0 : 1).ToList();

        var balances = new List<StockBalance>(ordered.Count);
        foreach (var row in ordered)
        {
            var locationType = row.QuantityDelta >= 0 ? row.ToLocationType! : row.FromLocationType!;
            var locationId = row.QuantityDelta >= 0 ? row.ToLocationId!.Value : row.FromLocationId!.Value;

            var stock = await _db.StockItems
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.LocationType == locationType && s.LocationId == locationId && s.ProductId == row.ProductId,
                    cancellationToken);

            balances.Add(new StockBalance(locationType, locationId, row.ProductId, stock?.Quantity ?? 0m));
        }

        return new MovementResult(
            MovementId: primary.Id,
            MovementIds: ordered.Select(m => m.Id).ToList(),
            Balances: balances,
            Replayed: true);
    }

    private async Task EnsureProductExistsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var exists = await _db.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("product", productId);
        }
    }

    private async Task EnsureLocationExistsAsync(string locationType, Guid locationId, CancellationToken cancellationToken)
    {
        var exists = locationType == StockLocation.Warehouse
            ? await _db.Warehouses.AnyAsync(w => w.Id == locationId, cancellationToken)
            : await _db.FieldInventories.AnyAsync(f => f.Id == locationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException(
                locationType == StockLocation.Warehouse ? "warehouse" : "field_inventory",
                locationId);
        }
    }

    private static bool IsIdempotencyViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg
        && pg.SqlState == "23505"
        && pg.ConstraintName == "ux_inventory_movements_env_idempotency";
}
