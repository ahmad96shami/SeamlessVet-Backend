using System.Security.Claims;
using Hangfire.Dashboard;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Jobs;

/// <summary>
/// Gates the Hangfire dashboard at <c>/hangfire</c> to admins only (M11 task 4, exit criterion). The
/// authorization decision lives in the pure <see cref="IsAuthorized(HttpContext)"/> helper so it can
/// be unit-tested without standing up the dashboard; this class is the thin Hangfire adapter.
/// </summary>
public sealed class AdminOnlyDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => IsAuthorized(context.GetHttpContext());

    /// <summary>True only for an authenticated principal whose role is <see cref="RoleKey.Admin"/>.</summary>
    public static bool IsAuthorized(HttpContext httpContext)
    {
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        return string.Equals(role, RoleKey.Admin, StringComparison.OrdinalIgnoreCase);
    }
}
