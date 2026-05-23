using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Notifications;

/// <summary>
/// Resolves recipient user ids for a notification within one environment. Used by domain-event
/// handlers and Hangfire jobs, both of which run without an HTTP principal — so every query bypasses
/// the env-scoped filter and scopes by an explicit <c>environmentId</c> instead, and only active,
/// non-deleted users are returned (PRD §9 maps each notification type to a recipient role set).
/// </summary>
public sealed class NotificationRecipientResolver
{
    private readonly ApplicationDbContext _db;

    public NotificationRecipientResolver(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<Guid>> AdminsAsync(Guid environmentId, CancellationToken cancellationToken)
        => ByRolesAsync(environmentId, [RoleKey.Admin], cancellationToken);

    public async Task<IReadOnlyList<Guid>> ByRolesAsync(
        Guid environmentId,
        IReadOnlyCollection<string> roleKeys,
        CancellationToken cancellationToken)
    {
        var ids = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.EnvironmentId == environmentId && u.DeletedAt == null && u.Status == UserStatus.Active)
            .Join(
                _db.Roles.IgnoreQueryFilters().Where(r => r.EnvironmentId == environmentId && roleKeys.Contains(r.Key)),
                u => u.RoleId,
                r => r.Id,
                (u, _) => u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids;
    }
}
