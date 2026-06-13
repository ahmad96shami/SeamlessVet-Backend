namespace VetSystem.Application.Identity;

/// <summary>
/// Resolves the operational status of a tenant environment, cached in-process (single-instance API;
/// no Redis backplane per TECH_STACK.md). The per-request <c>EnvironmentStatusMiddleware</c> reads
/// this to reject suspended/deleted tenants on already-issued JWTs; the platform console calls
/// <see cref="Invalidate"/> on suspend/reactivate so the gate flips within one request.
/// </summary>
public interface IEnvironmentStatusProvider
{
    /// <summary>
    /// Returns the environment's <c>status</c> (e.g. <c>active</c>/<c>suspended</c>), or
    /// <c>null</c> when the environment does not exist or is soft-deleted.
    /// </summary>
    Task<string?> GetStatusAsync(Guid environmentId, CancellationToken cancellationToken);

    void Invalidate(Guid environmentId);
}
