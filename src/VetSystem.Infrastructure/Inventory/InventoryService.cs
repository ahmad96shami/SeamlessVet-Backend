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
///
/// <para><b>M25 FEFO lots.</b> Each stock-arriving leg (<c>receive</c> / positive <c>adjust</c> /
/// <c>return_add</c>) creates an <see cref="InventoryLot"/> carrying its cost + expiry; each
/// stock-leaving leg (<c>sale_deduct</c> / negative <c>adjust</c>) FEFO-consumes earliest-expiry
/// lots and decrements their <c>remaining_qty</c> in the same transaction. A transfer
/// (<c>load_to_field</c> / <c>unload_from_field</c>) FEFO-consumes the source and <b>mirrors each
/// consumed sub-lot's cost + expiry at the destination</b>, so field stock keeps its FEFO basis.
/// Per (location, product) <c>Σ remaining_qty == stock_items.quantity</c> holds by construction.
/// <see cref="MovementResult.ResolvedUnitCost"/> hands the FEFO weighted-average cost back for the
/// caller to snapshot as COGS.</para>
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private const string SecondLegSuffix = ":2";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMovementTranslator _translator;
    private readonly IDomainEventPublisher _events;
    private readonly IClock _clock;
    private readonly IGuidV7Generator _ids;

    public InventoryService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMovementTranslator translator,
        IDomainEventPublisher events,
        IClock clock,
        IGuidV7Generator ids)
    {
        _db = db;
        _currentUser = currentUser;
        _translator = translator;
        _events = events;
        _clock = clock;
        _ids = ids;
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

        var product = await _db.Products
            .Select(p => new { p.Id, p.PurchasePrice, p.ExpirationDate })
            .FirstOrDefaultAsync(p => p.Id == intent.ProductId, cancellationToken)
            ?? throw new NotFoundException("product", intent.ProductId);

        // The cost basis for a created lot (receive / return / adjust-up) and for any FEFO shortfall.
        var fallbackUnitCost = intent.UnitCost ?? product.PurchasePrice;

        var movements = new List<InventoryMovement>(legs.Count);
        var balances = new List<StockBalance>(legs.Count);
        var resolvedUnitCost = fallbackUnitCost;
        FefoConsumptionPlan? transferSourcePlan = null; // leg-0 source consume → leg-1 destination mirror

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

            // --- M25 lot handling: arriving stock creates lots; leaving stock FEFO-consumes them. ---
            Guid? movementLotId = null;

            if (leg.SignedDelta < 0m)
            {
                var plan = await ConsumeFefoAsync(
                    envId, intent.ProductId, locationType, locationId,
                    -leg.SignedDelta, fallbackUnitCost, cancellationToken);
                movementLotId = plan.SingleLotId;
                resolvedUnitCost = plan.WeightedAverageUnitCost;

                // A transfer's leg-0 source consume drives the leg-1 destination lot mirroring.
                if (legs.Count == 2 && i == 0)
                {
                    transferSourcePlan = plan;
                }
            }
            else if (transferSourcePlan is { } sourcePlan)
            {
                // Transfer destination leg — recreate the consumed source sub-lots' cost + expiry so
                // field stock keeps its FEFO basis (one lot per source draw, plus any fallback remainder).
                movementLotId = MirrorTransferLots(envId, intent, locationType, locationId, sourcePlan);
            }
            else
            {
                // Standalone arrival (receive / return_add / positive adjust): one new lot.
                var lot = CreateLot(
                    envId, intent.ProductId, locationType, locationId,
                    fallbackUnitCost, intent.ExpirationDate, intent.LotNumber,
                    leg.SignedDelta, intent.PurchaseInvoiceItemId);
                movementLotId = lot.Id;
                resolvedUnitCost = lot.UnitCost;
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
                LotId = movementLotId,
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
            Replayed: false,
            ResolvedUnitCost: resolvedUnitCost);
    }

    /// <summary>
    /// FEFO-consumes <paramref name="quantity"/> from the location's on-hand lots, decrementing each
    /// drawn lot's <c>remaining_qty</c> on the tracked entity (persisted by the caller's SaveChanges).
    /// Returns the plan (draws + weighted-average cost) for COGS and transfer mirroring.
    /// </summary>
    private async Task<FefoConsumptionPlan> ConsumeFefoAsync(
        Guid envId, Guid productId, string locationType, Guid locationId,
        decimal quantity, decimal fallbackUnitCost, CancellationToken cancellationToken)
    {
        var lots = await _db.InventoryLots
            .Where(l => l.ProductId == productId
                        && l.LocationType == locationType
                        && l.LocationId == locationId
                        && l.RemainingQty > 0m)
            .ToListAsync(cancellationToken);

        var plan = FefoPlanner.Plan(
            lots.Select(l => new FefoLotView(l.Id, l.RemainingQty, l.UnitCost, l.ExpirationDate, l.ReceivedAt)).ToList(),
            quantity,
            fallbackUnitCost);

        if (plan.Draws.Count > 0)
        {
            var byId = lots.ToDictionary(l => l.Id);
            foreach (var draw in plan.Draws)
            {
                byId[draw.LotId].RemainingQty -= draw.Quantity;
            }
        }

        return plan;
    }

    /// <summary>Recreates a transfer's consumed source sub-lots at the destination (one lot per draw,
    /// plus a fallback-cost lot for any uncovered remainder). Returns the single created lot id when
    /// exactly one lot resulted, else null.</summary>
    private Guid? MirrorTransferLots(
        Guid envId, MovementIntent intent, string locationType, Guid locationId, FefoConsumptionPlan sourcePlan)
    {
        var created = new List<InventoryLot>(sourcePlan.Draws.Count + 1);

        foreach (var draw in sourcePlan.Draws)
        {
            created.Add(CreateLot(
                envId, intent.ProductId, locationType, locationId,
                draw.UnitCost, draw.ExpirationDate, intent.LotNumber, draw.Quantity, purchaseInvoiceItemId: null));
        }

        if (sourcePlan.Shortfall > 0m)
        {
            created.Add(CreateLot(
                envId, intent.ProductId, locationType, locationId,
                sourcePlan.FallbackUnitCost, expirationDate: null, intent.LotNumber, sourcePlan.Shortfall, purchaseInvoiceItemId: null));
        }

        return created.Count == 1 ? created[0].Id : null;
    }

    private InventoryLot CreateLot(
        Guid envId, Guid productId, string locationType, Guid locationId,
        decimal unitCost, DateOnly? expirationDate, string? lotNumber, decimal quantity, Guid? purchaseInvoiceItemId)
    {
        var lot = new InventoryLot
        {
            Id = _ids.New(),
            EnvironmentId = envId,
            ProductId = productId,
            LocationType = locationType,
            LocationId = locationId,
            PurchaseInvoiceItemId = purchaseInvoiceItemId,
            UnitCost = unitCost,
            ExpirationDate = expirationDate,
            LotNumber = lotNumber,
            ReceivedQty = quantity,
            RemainingQty = quantity,
            ReceivedAt = _clock.UtcNow,
        };
        _db.InventoryLots.Add(lot);
        return lot;
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

        // A replay does not recompute COGS — the original issuance already snapshotted it.
        return new MovementResult(
            MovementId: primary.Id,
            MovementIds: ordered.Select(m => m.Id).ToList(),
            Balances: balances,
            Replayed: true,
            ResolvedUnitCost: 0m);
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
