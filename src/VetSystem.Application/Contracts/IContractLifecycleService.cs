namespace VetSystem.Application.Contracts;

/// <summary>
/// Centralizes the contract status state machine (SCHEMA §5 / PRD §6.6): <c>draft → active | cancelled</c>,
/// <c>active → completed | cancelled</c>; <c>completed</c>/<c>cancelled</c> are terminal. The activation
/// gate (permission + online) is layered on top of this in the service — this only governs which
/// transitions are structurally legal.
/// </summary>
public interface IContractLifecycleService
{
    /// <summary>Throws a typed domain error when <paramref name="from"/> → <paramref name="to"/> is not a legal transition.</summary>
    void EnsureCanTransition(string from, string to);
}
