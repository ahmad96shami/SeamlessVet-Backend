using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;

namespace VetSystem.API.Notifications.Handlers;

/// <summary>
/// M11 task 12 — when a customer's ledger settles to zero (the last open invoice is paid), notifies
/// the environment's admins and accountants that the account is ready to close and release doctor
/// entitlements (PRD §9, settlement lock SCHEMA invariant #1).
/// </summary>
public sealed class AccountReadyForSettlementHandler : IDomainEventHandler<AccountReadyForSettlementEvent>
{
    private readonly NotificationRecipientResolver _recipients;
    private readonly INotificationDispatcher _dispatcher;

    public AccountReadyForSettlementHandler(NotificationRecipientResolver recipients, INotificationDispatcher dispatcher)
    {
        _recipients = recipients;
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(AccountReadyForSettlementEvent domainEvent, CancellationToken cancellationToken)
    {
        var recipients = await _recipients.ByRolesAsync(
            domainEvent.EnvironmentId, [RoleKey.Admin, RoleKey.Accountant], cancellationToken);

        await _dispatcher.DispatchAsync(
            new NotificationDispatch(
                domainEvent.EnvironmentId,
                recipients,
                NotificationType.AccountReadyForSettlement,
                Title: "حساب جاهز للتسوية",
                Body: "تم سداد كامل رصيد العميل؛ يمكن الآن إغلاق الحساب وتحرير استحقاقات الأطباء.",
                Payload: new
                {
                    domainEvent.CustomerId,
                    domainEvent.LedgerId,
                    domainEvent.PreviousBalance,
                }),
            cancellationToken);
    }
}
