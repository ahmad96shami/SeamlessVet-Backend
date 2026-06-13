using VetSystem.Application.Common;
using VetSystem.Application.Identity;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Middleware;

/// <summary>
/// Rejects authenticated <em>tenant</em> requests whose environment is suspended or soft-deleted, so
/// suspension bites already-issued JWTs (not just new logins). Runs after authentication so the
/// principal — and thus the <c>environment_id</c> claim — is populated.
///
/// <para>Bypassed for: anonymous requests (<c>/auth/*</c>, <c>/platform/auth/*</c>), platform tokens
/// (no <c>environment_id</c> claim), and infrastructure paths (<c>/platform</c>, <c>/health</c>,
/// <c>/swagger</c>, <c>/.well-known</c>, <c>/hangfire</c>). The canonical
/// <c>environment_suspended</c> 403 flows through <see cref="ExceptionHandlingMiddleware"/>.</para>
/// </summary>
public sealed class EnvironmentStatusMiddleware
{
    private static readonly string[] BypassPrefixes =
        ["/platform", "/health", "/swagger", "/.well-known", "/hangfire"];

    private readonly RequestDelegate _next;

    public EnvironmentStatusMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserAccessor currentUser,
        IEnvironmentStatusProvider statusProvider)
    {
        if (ShouldBypass(context, currentUser))
        {
            await _next(context);
            return;
        }

        var environmentId = currentUser.EnvironmentId!.Value;
        var status = await statusProvider.GetStatusAsync(environmentId, context.RequestAborted);
        if (status != EnvironmentStatus.Active)
        {
            throw new ForbiddenException(
                "environment_suspended",
                "This center is suspended. Contact the platform administrator.");
        }

        await _next(context);
    }

    private static bool ShouldBypass(HttpContext context, ICurrentUserAccessor currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.EnvironmentId is null)
        {
            // Anonymous endpoints, or a platform token (no environment_id claim) — not tenant-scoped.
            return true;
        }

        var path = context.Request.Path;
        foreach (var prefix in BypassPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
