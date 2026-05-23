namespace VetSystem.Application.Partnership;

/// <summary>
/// Enforces the cross-row partnership invariant (SCHEMA §1, task 4): per environment, the active
/// shares sum to ≤ 100% on every effective date. This cannot be expressed as a column CHECK, so it
/// lives in the service layer. The check is pure (no DB) — the caller loads the environment's live
/// shares, folds in the candidate window, and passes the lot here.
/// </summary>
public interface IPartnershipValidator
{
    /// <summary>
    /// Throws <see cref="VetSystem.Domain.Common.ConflictException"/> (<c>partnership_overallocated</c>)
    /// when the active shares exceed 100% on any date covered by <paramref name="shares"/>.
    /// </summary>
    void EnsureWithinLimit(IReadOnlyCollection<ShareWindow> shares);
}

/// <summary>A share's percent over a half-bounded date window; the unit the limit check works on.</summary>
public sealed record ShareWindow(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal SharePercent);
