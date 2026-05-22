using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.API.Filters;

/// <summary>
/// Permission gate applied at route-group level alongside <c>RequireAuthorization()</c>.
/// M0 reads the user's permissions straight off <see cref="ICurrentUserAccessor.Permissions"/>
/// (the <c>perms</c> claim, populated once M1 issues access tokens). M1/18 rewires this filter
/// to consult <c>IPermissionResolver</c> so role + per-user-override resolution is centralized.
/// </summary>
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
        if (!user.IsAuthenticated)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        if (!user.Permissions.Contains(_permission, StringComparer.OrdinalIgnoreCase))
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
