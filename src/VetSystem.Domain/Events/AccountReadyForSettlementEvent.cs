using VetSystem.Domain.Common;

namespace VetSystem.Domain.Events;

/// <summary>
/// Raised when a customer's ledger balance transitions from owing (&gt; 0) to fully settled (0) —
/// i.e. the last open invoice has been paid (PRD §9, M11 task 12). At this point the account can be
/// closed and its doctor entitlements released (settlement lock, SCHEMA invariant #1). M11 fans this
/// out to the environment's admins/accountants so they know an account is ready to close.
/// </summary>
public sealed record AccountReadyForSettlementEvent(
    Guid EnvironmentId,
    Guid CustomerId,
    Guid LedgerId,
    decimal PreviousBalance) : IDomainEvent;
