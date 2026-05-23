using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;

namespace VetSystem.API.Notifications.Handlers;

/// <summary>
/// M11 task 14 — turns a rejected negative-stock movement (raised by M4's
/// <see cref="NegativeStockAttemptedEvent"/>) into a realtime alert for the doctor who attempted it
/// and the environment's admins (PRD §9). The publisher runs this in a fresh scope, so the
/// notification persists even though the offending inventory write was rolled back.
/// </summary>
public sealed class NegativeStockAttemptedHandler : IDomainEventHandler<NegativeStockAttemptedEvent>
{
    private readonly NotificationRecipientResolver _recipients;
    private readonly INotificationDispatcher _dispatcher;

    public NegativeStockAttemptedHandler(NotificationRecipientResolver recipients, INotificationDispatcher dispatcher)
    {
        _recipients = recipients;
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(NegativeStockAttemptedEvent domainEvent, CancellationToken cancellationToken)
    {
        var admins = await _recipients.AdminsAsync(domainEvent.EnvironmentId, cancellationToken);
        var recipients = admins.Append(domainEvent.PerformedBy).Distinct().ToList();

        var location = domainEvent.LocationType == StockLocation.Warehouse ? "المستودع" : "المخزون الميداني";

        await _dispatcher.DispatchAsync(
            new NotificationDispatch(
                domainEvent.EnvironmentId,
                recipients,
                NotificationType.NegativeStock,
                Title: "محاولة سحب مخزون بالسالب",
                Body: $"حركة مرفوضة: الكمية المطلوبة تتجاوز الرصيد المتاح في {location}.",
                Payload: new
                {
                    domainEvent.ProductId,
                    domainEvent.LocationType,
                    domainEvent.LocationId,
                    domainEvent.AttemptedDelta,
                    domainEvent.CurrentQuantity,
                    domainEvent.VisitId,
                    domainEvent.PerformedBy,
                }),
            cancellationToken);
    }
}
