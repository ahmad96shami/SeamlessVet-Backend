using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Notifications;

/// <summary>
/// The in-app feed for the current user (M11 task 8): list this user's notifications (env-scoped by
/// the global query filter, then narrowed to the caller's own id) and mark one read. Marking is
/// idempotent — a re-read of an already-read row is a no-op. Realtime delivery is the hub's job; this
/// is the durable history the client renders and reconciles against.
/// </summary>
public sealed class NotificationsService
{
    private const int MaxPageSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;

    public NotificationsService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NotificationPayload>> ListAsync(
        bool unreadOnly, int? skip, int? take, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        var query = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(ToPayload).ToList();
    }

    public async Task<NotificationPayload> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        var row = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
                  ?? throw new NotFoundException("notification", id);

        if (row.ReadAt is null)
        {
            row.ReadAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToPayload(row);
    }

    private static NotificationPayload ToPayload(Notification n) => new(
        n.Id,
        n.Type,
        n.Title,
        n.Body,
        n.Payload is null ? null : JsonSerializer.Deserialize<JsonElement>(n.Payload),
        n.CreatedAt,
        n.ReadAt);

    private Guid RequireUser()
    {
        if (_currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return userId;
    }
}
