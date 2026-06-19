using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Notifications;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Notifications.Handlers;

/// <summary>
/// Turns a <see cref="VisitAssignedEvent"/> (a visit created for a doctor by someone else — typically
/// a receptionist) into a realtime, per-user alert for that doctor so they know a visit is waiting.
/// The publisher runs this in a fresh scope; it is best-effort, so a delivery hiccup never affects the
/// visit create. The <c>Payload</c> carries the visit id so a tap deep-links straight into the visit.
/// </summary>
public sealed class VisitAssignedHandler : IDomainEventHandler<VisitAssignedEvent>
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationDispatcher _dispatcher;

    public VisitAssignedHandler(ApplicationDbContext db, INotificationDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(VisitAssignedEvent domainEvent, CancellationToken cancellationToken)
    {
        // Explicit env filter (not the ambient query filter) so the lookup is correct in the fresh scope.
        var customerName = await _db.Customers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.Id == domainEvent.CustomerId && c.EnvironmentId == domainEvent.EnvironmentId)
            .Select(c => c.FullName)
            .FirstOrDefaultAsync(cancellationToken);

        var owner = string.IsNullOrWhiteSpace(customerName) ? "العميل" : customerName;
        var numberPart = string.IsNullOrWhiteSpace(domainEvent.VisitNumber)
            ? string.Empty
            : $" رقم {domainEvent.VisitNumber}";

        await _dispatcher.DispatchAsync(
            new NotificationDispatch(
                domainEvent.EnvironmentId,
                [domainEvent.DoctorId],
                NotificationType.VisitAssigned,
                Title: "تم تعيينك لزيارة جديدة",
                Body: $"تم تعيينك للزيارة{numberPart} للعميل {owner}.",
                Payload: new
                {
                    domainEvent.VisitId,
                    domainEvent.VisitNumber,
                    domainEvent.CustomerId,
                    domainEvent.VisitType,
                    domainEvent.AssignedByUserId,
                }),
            cancellationToken);
    }
}
