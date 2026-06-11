using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;

namespace VetSystem.API.Notifications.Handlers;

/// <summary>
/// M30 (M11 task 13) — pushes a realtime notification to the doctor when a batch settlement credits
/// their supervision-fee entitlement to their doctor-partner balance (PRD §9).
/// </summary>
public sealed class EntitlementCreditedHandler : IDomainEventHandler<EntitlementCreditedEvent>
{
    private readonly INotificationDispatcher _dispatcher;

    public EntitlementCreditedHandler(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(EntitlementCreditedEvent domainEvent, CancellationToken cancellationToken)
    {
        await _dispatcher.DispatchAsync(
            new NotificationDispatch(
                domainEvent.EnvironmentId,
                [domainEvent.DoctorId],
                NotificationType.EntitlementCredited,
                Title: "تم تقييد استحقاقك",
                Body: $"تم تقييد استحقاق بقيمة {domainEvent.CreditAmount:0.00} إلى حسابك بعد تصفية الدورة.",
                Payload: new
                {
                    domainEvent.EntitlementId,
                    domainEvent.BatchId,
                    domainEvent.CreditAmount,
                }),
            cancellationToken);
    }
}
