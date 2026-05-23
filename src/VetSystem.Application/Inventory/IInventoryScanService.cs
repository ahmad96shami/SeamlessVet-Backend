using VetSystem.Application.Inventory.Contracts;

namespace VetSystem.Application.Inventory;

/// <summary>
/// Read-only inventory scans (M4 task 13). Returns <i>data only</i> — the alert dispatch
/// (notifications, SMS/WhatsApp) is the M11 Hangfire job's job. Both scans take an explicit
/// <c>environmentId</c> so a background worker (no HTTP principal) can run them across every
/// environment; they bypass the env query filter and scope explicitly.
/// </summary>
public interface IInventoryScanService
{
    /// <summary>(location, product) balances at or below their low-stock threshold.</summary>
    Task<IReadOnlyList<LowStockItem>> ScanLowStockAsync(Guid environmentId, CancellationToken cancellationToken);

    /// <summary>Products on hand expiring within <c>system_settings.expiration_warning_days</c>.</summary>
    Task<IReadOnlyList<ExpiringProduct>> ScanApproachingExpirationAsync(Guid environmentId, CancellationToken cancellationToken);
}
