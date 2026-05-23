using Microsoft.EntityFrameworkCore;
using VetSystem.API.Notifications;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M11 task 10 — daily low-stock scan against each environment's
/// <c>system_settings.low_stock_threshold_pct</c> (data comes from M4's <see cref="IInventoryScanService"/>).
/// Admins and inventory staff get the full picture; a field doctor additionally gets the items low in
/// their own field inventory (PRD §9). Aggregated to one notification per recipient and idempotent
/// per day.
/// </summary>
public sealed class LowStockAlertsJob
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryScanService _scan;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationRecipientResolver _recipients;
    private readonly IClock _clock;

    public LowStockAlertsJob(
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
            var items = await _scan.ScanLowStockAsync(environmentId, cancellationToken);
            if (items.Count == 0)
            {
                continue;
            }

            var admins = await _recipients.AdminsAsync(environmentId, cancellationToken);
            var inventoryStaff = await _recipients.ByRolesAsync(environmentId, [RoleKey.InventoryStaff], cancellationToken);

            var byRecipient = new Dictionary<Guid, List<LowStockItem>>();
            void Add(Guid userId, LowStockItem item)
            {
                if (!byRecipient.TryGetValue(userId, out var list))
                {
                    list = [];
                    byRecipient[userId] = list;
                }

                list.Add(item);
            }

            foreach (var item in items)
            {
                foreach (var adminId in admins)
                {
                    Add(adminId, item);
                }

                foreach (var staffId in inventoryStaff)
                {
                    Add(staffId, item);
                }

                if (item.LocationType == StockLocation.Field)
                {
                    var doctorId = await _db.FieldInventories
                        .IgnoreQueryFilters()
                        .Where(f => f.Id == item.LocationId)
                        .Select(f => (Guid?)f.DoctorId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (doctorId is { } fieldDoctor)
                    {
                        Add(fieldDoctor, item);
                    }
                }
            }

            var alreadyNotified = (await _db.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.EnvironmentId == environmentId
                            && n.Type == NotificationType.LowStock
                            && n.CreatedAt >= todayStart)
                .Select(n => n.UserId)
                .ToListAsync(cancellationToken)).ToHashSet();

            foreach (var (userId, list) in byRecipient)
            {
                if (alreadyNotified.Contains(userId))
                {
                    continue;
                }

                await _dispatcher.DispatchAsync(
                    new NotificationDispatch(
                        environmentId,
                        [userId],
                        NotificationType.LowStock,
                        Title: "تنبيه انخفاض المخزون",
                        Body: $"يوجد {list.Count} صنف عند حد إعادة الطلب أو دونه.",
                        Payload: new
                        {
                            Items = list.Select(i => new
                            {
                                i.ProductId,
                                i.ProductNameAr,
                                i.LocationType,
                                i.LocationId,
                                i.Quantity,
                                i.ReorderPoint,
                            }).ToList(),
                        }),
                    cancellationToken);
            }
        }
    }
}
