using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;

namespace VetSystem.API.Notifications.Handlers;

/// <summary>
/// M11 task 13 — pushes a realtime notification to the entitled doctor when their
/// <c>doctor_entitlements</c> row is approved for payout (PRD §9). Fired off the M9 approve action,
/// which only succeeds once the settlement lock is satisfied (the account closed in full).
/// </summary>
public sealed class EntitlementApprovedHandler : IDomainEventHandler<EntitlementApprovedEvent>
{
    private readonly INotificationDispatcher _dispatcher;

    public EntitlementApprovedHandler(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(EntitlementApprovedEvent domainEvent, CancellationToken cancellationToken)
    {
        await _dispatcher.DispatchAsync(
            new NotificationDispatch(
                domainEvent.EnvironmentId,
                [domainEvent.DoctorId],
                NotificationType.EntitlementApproved,
                Title: "تمت الموافقة على استحقاقك",
                Body: $"تمت الموافقة على استحقاق بقيمة {domainEvent.ComputedAmount:0.00} وهو جاهز للصرف.",
                Payload: new
                {
                    domainEvent.EntitlementId,
                    domainEvent.ComputedAmount,
                }),
            cancellationToken);
    }
}
