using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Inventory;

/// <summary>
/// One leg of a movement: a signed delta to apply at exactly one location. The <i>affected</i>
/// location is <c>To</c> when the delta is positive (stock arriving) and <c>From</c> when negative
/// (stock leaving) — see <see cref="InventoryMovement"/>. A persisted <c>inventory_movements</c>
/// row is written per leg, so <c>stock_items.quantity == Σ quantity_delta</c> over the rows whose
/// affected location is that (location, product) pair holds by construction.
/// </summary>
public sealed record PlannedMovementLeg(
    string? FromLocationType,
    Guid? FromLocationId,
    string? ToLocationType,
    Guid? ToLocationId,
    decimal SignedDelta)
{
    public string AffectedLocationType => SignedDelta >= 0 ? ToLocationType! : FromLocationType!;

    public Guid AffectedLocationId => SignedDelta >= 0 ? ToLocationId!.Value : FromLocationId!.Value;
}

/// <summary>
/// Translates a high-level <see cref="MovementIntent"/> into the signed leg(s) to apply
/// (M4 task 5). Pure (no I/O) so the per-type delta math is unit-tested without a database.
/// Single-location types yield one leg; <c>load_to_field</c>/<c>unload_from_field</c> yield two
/// (a negative source leg + a positive destination leg). Rejects structurally invalid intents
/// with a typed <see cref="ConflictException"/>.
/// </summary>
public interface IMovementTranslator
{
    IReadOnlyList<PlannedMovementLeg> Translate(MovementIntent intent);
}

public sealed class MovementTranslator : IMovementTranslator
{
    public IReadOnlyList<PlannedMovementLeg> Translate(MovementIntent intent)
    {
        if (!MovementType.All.Contains(intent.MovementType))
        {
            throw new ConflictException("invalid_movement_type",
                $"movement_type '{intent.MovementType}' is not one of: {string.Join(", ", MovementType.All)}.");
        }

        return intent.MovementType switch
        {
            MovementType.Receive => Receive(intent),
            MovementType.ReturnAdd => ReturnAdd(intent),
            MovementType.SaleDeduct => SaleDeduct(intent),
            MovementType.Adjust => Adjust(intent),
            MovementType.LoadToField => LoadToField(intent),
            MovementType.UnloadFromField => UnloadFromField(intent),
            _ => throw new ConflictException("invalid_movement_type", intent.MovementType),
        };
    }

    private static IReadOnlyList<PlannedMovementLeg> Receive(MovementIntent intent)
    {
        RequirePositive(intent.Quantity, intent.MovementType);
        var (toType, toId) = RequireLocation(intent.ToLocationType, intent.ToLocationId, "to", intent.MovementType);
        RequireWarehouse(toType, "to", intent.MovementType);
        return [new PlannedMovementLeg(null, null, toType, toId, intent.Quantity)];
    }

    private static IReadOnlyList<PlannedMovementLeg> ReturnAdd(MovementIntent intent)
    {
        RequirePositive(intent.Quantity, intent.MovementType);
        var (toType, toId) = RequireLocation(intent.ToLocationType, intent.ToLocationId, "to", intent.MovementType);
        return [new PlannedMovementLeg(null, null, toType, toId, intent.Quantity)];
    }

    private static IReadOnlyList<PlannedMovementLeg> SaleDeduct(MovementIntent intent)
    {
        RequirePositive(intent.Quantity, intent.MovementType);
        var (fromType, fromId) = RequireLocation(intent.FromLocationType, intent.FromLocationId, "from", intent.MovementType);
        return [new PlannedMovementLeg(fromType, fromId, null, null, -intent.Quantity)];
    }

    private static IReadOnlyList<PlannedMovementLeg> Adjust(MovementIntent intent)
    {
        if (intent.Quantity == 0m)
        {
            throw new ConflictException("invalid_quantity", "adjust quantity_delta must be non-zero (signed).");
        }

        // The adjust target is supplied in the 'to' slot; a negative delta moves it to 'from' so the
        // affected-location rule (delta < 0 ⇒ from) holds.
        var (locType, locId) = RequireLocation(intent.ToLocationType, intent.ToLocationId, "to", intent.MovementType);
        return intent.Quantity > 0m
            ? [new PlannedMovementLeg(null, null, locType, locId, intent.Quantity)]
            : [new PlannedMovementLeg(locType, locId, null, null, intent.Quantity)];
    }

    private static IReadOnlyList<PlannedMovementLeg> LoadToField(MovementIntent intent)
    {
        RequirePositive(intent.Quantity, intent.MovementType);
        var (fromType, fromId) = RequireLocation(intent.FromLocationType, intent.FromLocationId, "from", intent.MovementType);
        var (toType, toId) = RequireLocation(intent.ToLocationType, intent.ToLocationId, "to", intent.MovementType);
        RequireWarehouse(fromType, "from", intent.MovementType);
        RequireField(toType, "to", intent.MovementType);

        return
        [
            new PlannedMovementLeg(fromType, fromId, toType, toId, -intent.Quantity), // debit warehouse
            new PlannedMovementLeg(fromType, fromId, toType, toId, intent.Quantity),  // credit field
        ];
    }

    private static IReadOnlyList<PlannedMovementLeg> UnloadFromField(MovementIntent intent)
    {
        RequirePositive(intent.Quantity, intent.MovementType);
        var (fromType, fromId) = RequireLocation(intent.FromLocationType, intent.FromLocationId, "from", intent.MovementType);
        var (toType, toId) = RequireLocation(intent.ToLocationType, intent.ToLocationId, "to", intent.MovementType);
        RequireField(fromType, "from", intent.MovementType);
        RequireWarehouse(toType, "to", intent.MovementType);

        return
        [
            new PlannedMovementLeg(fromType, fromId, toType, toId, -intent.Quantity), // debit field
            new PlannedMovementLeg(fromType, fromId, toType, toId, intent.Quantity),  // credit warehouse
        ];
    }

    private static void RequirePositive(decimal quantity, string movementType)
    {
        if (quantity <= 0m)
        {
            throw new ConflictException("invalid_quantity",
                $"{movementType} quantity must be greater than zero.");
        }
    }

    private static (string Type, Guid Id) RequireLocation(string? type, Guid? id, string slot, string movementType)
    {
        if (string.IsNullOrWhiteSpace(type) || id is not { } locId || locId == Guid.Empty)
        {
            throw new ConflictException("invalid_location",
                $"{movementType} requires a '{slot}' location (type + id).");
        }

        if (!StockLocation.All.Contains(type))
        {
            throw new ConflictException("invalid_location",
                $"{movementType} '{slot}' location_type '{type}' is not one of: {string.Join(", ", StockLocation.All)}.");
        }

        return (type, locId);
    }

    private static void RequireWarehouse(string type, string slot, string movementType)
    {
        if (type != StockLocation.Warehouse)
        {
            throw new ConflictException("invalid_location",
                $"{movementType} requires the '{slot}' location to be a warehouse.");
        }
    }

    private static void RequireField(string type, string slot, string movementType)
    {
        if (type != StockLocation.Field)
        {
            throw new ConflictException("invalid_location",
                $"{movementType} requires the '{slot}' location to be a field inventory.");
        }
    }
}
