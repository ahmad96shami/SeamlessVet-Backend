namespace VetSystem.Application.Common;

/// <summary>
/// Resolves the effective permission set for a user = role defaults UNION grants − denies
/// (PRD §3 "RBAC with per-user permission overrides"). Cached per-user; M1 task 17 requires
/// invalidation when role or override mutations land.
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlySet<string>> ResolveAsync(Guid userId, Guid environmentId, CancellationToken cancellationToken);

    void Invalidate(Guid userId);
}
