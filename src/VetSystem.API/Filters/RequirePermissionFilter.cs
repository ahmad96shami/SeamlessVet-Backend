using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.API.Filters;

/// <summary>
/// Permission gate applied at route-group level alongside <c>RequireAuthorization()</c>.
/// Resolves the user's effective permissions via <see cref="IPermissionResolver"/> (role
/// defaults + per-user overrides, cached for 5 minutes — invalidate from admin endpoints
/// when overrides change).
/// </summary>
/// <remarks>
/// <b>Policy-name convention.</b> Use the dot-notation permission key from
/// <c>VetSystem.Domain.Entities.PermissionKey</c> directly — e.g.
/// <c>group.RequirePermission(PermissionKey.UsersApprove)</c>. The key is the policy name.
/// </remarks>
public sealed class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequirePermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>();
        if (!user.IsAuthenticated || user.UserId is not { } userId || user.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var resolver = context.HttpContext.RequestServices.GetRequiredService<IPermissionResolver>();
        var perms = await resolver.ResolveAsync(userId, envId, context.HttpContext.RequestAborted);

        if (!perms.Contains(_permission))
        {
            throw new ForbiddenException("missing_permission", $"Required permission '{_permission}' is not granted.");
        }

        return await next(context);
    }
}

public static class RequirePermissionExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequirePermissionFilter(permission));
        return builder;
    }
}
