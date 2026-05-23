using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VetSystem.API.Identity;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Notifications;

/// <summary>
/// The single in-process realtime hub (TECH_STACK — no Redis backplane). Authenticated via the same
/// JWT bearer scheme as the REST API (the access token rides the connection query string; see the
/// JwtBearer <c>OnMessageReceived</c> wiring in <c>Program.cs</c>). On connect, a client auto-joins
/// its per-user group, its environment group, and — for admins — the environment's admin group, so
/// the dispatcher can target any of them while staying environment-isolated (M11 tasks 5, 6).
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub<INotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;
        var environmentId = user?.FindFirst(HttpCurrentUserAccessor.EnvironmentIdClaim)?.Value;
        var role = user?.FindFirst(ClaimTypes.Role)?.Value ?? user?.FindFirst("role")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.User(userId));
        }

        if (!string.IsNullOrEmpty(environmentId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.Environment(environmentId));

            if (string.Equals(role, RoleKey.Admin, StringComparison.OrdinalIgnoreCase))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.Admins(environmentId));
            }
        }

        await base.OnConnectedAsync();
    }
}
