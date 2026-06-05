using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Devices.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Devices;

/// <summary>
/// M21 — self-scoped device-token registry. Register upserts BY TOKEN: a shared device that
/// re-registers under whoever just signed in is reassigned, not duplicated (the token is globally
/// unique — one physical device, one row). Unregister deletes only the caller's own row and is
/// idempotent. <c>DeviceToken</c> is a plain POCO, so id/timestamps are stamped here explicitly
/// (no auditing interceptor) and deletes are hard.
/// </summary>
public sealed class DeviceTokensService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly IGuidV7Generator _ids;

    public DeviceTokensService(
        ApplicationDbContext db, ICurrentUserAccessor currentUser, IClock clock, IGuidV7Generator ids)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _ids = ids;
    }

    public async Task RegisterAsync(RegisterPushTokenRequest request, CancellationToken cancellationToken)
    {
        var (userId, environmentId) = RequireUser();
        var now = _clock.UtcNow;

        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);
        if (existing is not null)
        {
            existing.UserId = userId;
            existing.EnvironmentId = environmentId;
            existing.Platform = request.Platform;
            existing.LastSeenAt = now;
        }
        else
        {
            _db.DeviceTokens.Add(new DeviceToken
            {
                Id = _ids.New(),
                UserId = userId,
                EnvironmentId = environmentId,
                Token = request.Token,
                Platform = request.Platform,
                CreatedAt = now,
                LastSeenAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnregisterAsync(UnregisterPushTokenRequest request, CancellationToken cancellationToken)
    {
        var (userId, _) = RequireUser();

        // Own-token only: a stale token that meanwhile re-registered to another user is NOT yours
        // to remove — the filter makes the delete a silent no-op in that case (idempotent contract).
        await _db.DeviceTokens
            .Where(t => t.Token == request.Token && t.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private (Guid UserId, Guid EnvironmentId) RequireUser()
    {
        if (_currentUser.UserId is not { } userId || _currentUser.EnvironmentId is not { } environmentId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return (userId, environmentId);
    }
}
