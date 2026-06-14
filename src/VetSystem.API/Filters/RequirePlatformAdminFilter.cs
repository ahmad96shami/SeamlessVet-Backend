using System.Security.Claims;
using VetSystem.API.Identity;
using VetSystem.Domain.Common;

namespace VetSystem.API.Filters;

/// <summary>
/// M35 — gate applied at the <c>/platform/*</c> route-group level alongside
/// <c>RequireAuthorization()</c>. Mirrors <see cref="RequirePermissionFilter"/> but checks the
/// <c>platform_admin</c> claim instead of a tenant permission: a tenant token (which carries no such
/// claim) is rejected with <c>platform_admin_required</c>, completing the platform↔tenant isolation
/// (the env-scoped query filter + <c>RequirePermissionFilter</c>'s null-env guard block the reverse).
/// </summary>
public sealed class RequirePlatformAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var principal = context.HttpContext.User;
        var isPlatformAdmin = principal.Identity?.IsAuthenticated == true
            && principal.FindFirstValue(HttpCurrentUserAccessor.PlatformAdminClaim) == "true";

        if (!isPlatformAdmin)
        {
            throw new ForbiddenException("platform_admin_required", "A platform administrator token is required.");
        }

        return await next(context);
    }
}

public static class RequirePlatformAdminExtensions
{
    public static TBuilder RequirePlatformAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequirePlatformAdminFilter());
        return builder;
    }
}
