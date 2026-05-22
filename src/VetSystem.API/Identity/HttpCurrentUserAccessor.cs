using System.Security.Claims;
using VetSystem.Application.Common;

namespace VetSystem.API.Identity;

public sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    public const string EnvironmentIdClaim = "environment_id";
    public const string PermissionsClaim = "perms";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var sub = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? Principal?.FindFirst("sub")?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public Guid? EnvironmentId
    {
        get
        {
            var raw = Principal?.FindFirst(EnvironmentIdClaim)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Role => Principal?.FindFirst(ClaimTypes.Role)?.Value
                           ?? Principal?.FindFirst("role")?.Value;

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(PermissionsClaim).Select(c => c.Value).ToArray()
        ?? Array.Empty<string>();
}
