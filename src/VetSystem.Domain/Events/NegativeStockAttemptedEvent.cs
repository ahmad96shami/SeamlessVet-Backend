using VetSystem.Domain.Common;

namespace VetSystem.Domain.Events;

/// <summary>
/// Raised when a movement would drive a location's <c>stock_items.quantity</c> below zero
/// (M4 task 12). The offending write is rejected (no row is persisted); this event is published
/// first so M11 can fan it out as a realtime notification to the responsible doctor + Admin
/// (SCHEMA "Key invariants" #2 — inventory is delta-only and never goes negative).
/// </summary>
public sealed record NegativeStockAttemptedEvent(
    Guid EnvironmentId,
    Guid ProductId,
    string LocationType,
    Guid LocationId,
    decimal AttemptedDelta,
    decimal CurrentQuantity,
    Guid PerformedBy,
    Guid? VisitId) : IDomainEvent;
