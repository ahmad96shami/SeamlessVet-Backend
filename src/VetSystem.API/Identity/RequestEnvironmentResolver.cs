using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Identity;

/// <summary>
/// Resolves the environment for the current request. Order:
/// (1) JWT <c>environment_id</c> claim (authenticated routes — auth/me, /admin/*, /sync/*);
/// (2) <c>X-Environment-Id</c> header (unauthenticated /auth/register, /auth/login);
/// (3) <see cref="DataSeeder.BootstrapEnvironmentId"/> as the dev-time default for solo deployments.
/// </summary>
public interface IRequestEnvironmentResolver
{
    Guid Resolve();
}

public sealed class RequestEnvironmentResolver : IRequestEnvironmentResolver
{
    public const string HeaderName = "X-Environment-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserAccessor _currentUser;

    public RequestEnvironmentResolver(IHttpContextAccessor httpContextAccessor, ICurrentUserAccessor currentUser)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
    }

    public Guid Resolve()
    {
        if (_currentUser.EnvironmentId is { } fromJwt)
        {
            return fromJwt;
        }

        var http = _httpContextAccessor.HttpContext;
        if (http is not null
            && http.Request.Headers.TryGetValue(HeaderName, out var raw)
            && Guid.TryParse(raw.ToString(), out var fromHeader))
        {
            return fromHeader;
        }

        return DataSeeder.BootstrapEnvironmentId;
    }
}
