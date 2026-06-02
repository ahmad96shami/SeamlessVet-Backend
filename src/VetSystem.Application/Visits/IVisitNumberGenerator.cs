namespace VetSystem.Application.Visits;

/// <summary>
/// Auto-assigns a <c>visit_number</c> for web-created visits where the client doesn't supply one.
/// Mobile/field doctors mint numbers client-side from their offline-stored prefix; web users have
/// no offline state, so the server fills in <c>'{users.number_prefix}-{maxSeq + 1}'</c> when the
/// creator has a prefix configured, and otherwise returns <c>null</c> (the column stays nullable —
/// multiple NULLs are fine under the unique index per SCHEMA "Key invariants" #9).
/// </summary>
public interface IVisitNumberGenerator
{
    /// <summary>
    /// Returns the next available <c>{prefix}-{n}</c> for the current user, or <c>null</c> if the
    /// user has no <c>NumberPrefix</c>. Caller is expected to be inside the visit-create transaction;
    /// the unique-index race (between sequence read and insert) is handled by retrying the create
    /// when the DB throws <c>ux_visits_env_number</c>.
    /// </summary>
    Task<string?> NextForCurrentUserAsync(CancellationToken cancellationToken);
}
