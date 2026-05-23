using VetSystem.Application.Inventory.Contracts;

namespace VetSystem.Application.Inventory;

/// <summary>
/// SCHEMA "Key invariants" #2 — the single server-side chokepoint for stock changes. Clients send
/// a <see cref="MovementIntent"/> (a signed delta or a transfer); the server translates it into
/// append-only <c>inventory_movements</c> row(s) and recomputes the materialized
/// <c>stock_items.quantity</c> atomically. Absolute quantities are never written. A movement that
/// would drive any location negative is rejected and a <c>NegativeStockAttemptedEvent</c> is
/// published first. Idempotent: a replayed <see cref="MovementIntent.IdempotencyKey"/> returns the
/// original outcome without double-applying.
/// </summary>
public interface IInventoryService
{
    Task<MovementResult> ApplyMovementAsync(MovementIntent intent, CancellationToken cancellationToken);
}
