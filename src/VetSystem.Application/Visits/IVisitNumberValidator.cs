namespace VetSystem.Application.Visits;

/// <summary>
/// Validates a client-supplied <c>visit_number</c> (SCHEMA "Key invariants" #9). Numbers are
/// generated client-side as <c>'{users.number_prefix}-{sequence}'</c> so offline devices never
/// collide. The server checks three things: the format, that the prefix belongs to the
/// authenticated creator (a device cannot mint numbers under another user's prefix), and that the
/// number is unique per environment. The DB also enforces uniqueness via
/// <c>ux_visits_env_number</c>; this validator gives a typed error before hitting that constraint
/// and is the single place visit-number rules live (re-used by the dedicated endpoint and sync).
/// </summary>
public interface IVisitNumberValidator
{
    /// <summary>
    /// Throws a typed domain error if <paramref name="visitNumber"/> is malformed, carries a prefix
    /// that isn't the current user's, or already exists in the environment. <paramref name="excludeVisitId"/>
    /// lets an update keep its own number (the row already holds it).
    /// </summary>
    Task ValidateAsync(string visitNumber, Guid? excludeVisitId, CancellationToken cancellationToken);
}
