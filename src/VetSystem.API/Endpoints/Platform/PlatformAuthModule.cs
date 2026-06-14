using VetSystem.API.Filters;
using VetSystem.API.Identity;
using VetSystem.Application.Platform.Contracts;

namespace VetSystem.API.Endpoints.Platform;

/// <summary>
/// M35 — platform super-admin authentication, outside any tenant. <c>POST /platform/auth/login</c>
/// exchanges a global phone + password for a <c>platform_admin</c> access token (no refresh in v1).
/// Anonymous + IP-rate-limited ("auth" policy), like <c>/auth/*</c>.
/// </summary>
public sealed class PlatformAuthModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/platform/auth").WithTags("Platform");

        group.MapPost("/login", Login)
            .AddEndpointFilter<ValidationFilter<PlatformLoginRequest>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Platform_Login")
            .WithSummary("Exchange a platform phone + password for a platform-admin access token.");
    }

    private static async Task<IResult> Login(
        PlatformLoginRequest request,
        PlatformAuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);
        return TypedResults.Ok(result);
    }
}
