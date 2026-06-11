using Microsoft.EntityFrameworkCore;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M11 task 11 — daily scan for products on hand expiring within
/// <c>system_settings.expiration_warning_days</c> (data from M4's <see cref="IInventoryScanService"/>).
/// Notifies admins and inventory staff (PRD §9). One notification listing the expiring items per
/// recipient, idempotent per day.
/// </summary>
public sealed class ExpirationWarningsJob
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryScanService _scan;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationRecipientResolver _recipients;
    private readonly IClock _clock;

    public ExpirationWarningsJob(
        ApplicationDbContext db,
        IInventoryScanService scan,
        INotificationDispatcher dispatcher,
        NotificationRecipientResolver recipients,
        IClock clock)
    {
        _db = db;
        _scan = scan;
        _dispatcher = dispatcher;
        _recipients = recipients;
        _clock = clock;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var todayStart = JobHelpers.StartOfTodayUtc(_clock);

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            var items = await _scan.ScanApproachingExpirationAsync(environmentId, cancellationToken);
            if (items.Count == 0)
            {
                continue;
            }

            var admins = await _recipients.AdminsAsync(environmentId, cancellationToken);
            var inventoryStaff = await _recipients.ByRolesAsync(environmentId, [RoleKey.InventoryStaff], cancellationToken);
            var recipients = admins.Concat(inventoryStaff).Distinct().ToList();

            var alreadyNotified = (await _db.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.EnvironmentId == environmentId
                            && n.Type == NotificationType.ExpiryWarning
                            && n.CreatedAt >= todayStart)
                .Select(n => n.UserId)
                .ToListAsync(cancellationToken)).ToHashSet();

            var targets = recipients.Where(r => !alreadyNotified.Contains(r)).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            await _dispatcher.DispatchAsync(
                new NotificationDispatch(
                    environmentId,
                    targets,
                    NotificationType.ExpiryWarning,
                    Title: "تنبيه قرب انتهاء الصلاحية",
                    Body: $"يوجد {items.Count} دفعة (Lot) تقترب صلاحيتها من الانتهاء.",
                    Payload: new
                    {
                        Items = items.Select(i => new
                        {
                            i.LotId,
                            i.ProductId,
                            i.ProductNameAr,
                            i.LotNumber,
                            i.ExpirationDate,
                            i.DaysUntilExpiry,
                            i.NearExpiryQuantity,
                            i.QuantityOnHand,
                        }).ToList(),
                    }),
                cancellationToken);
        }
    }
}
