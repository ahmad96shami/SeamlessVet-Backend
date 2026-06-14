using VetSystem.API.Filters;
using VetSystem.API.Identity;
using VetSystem.Application.Common;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Auth;

/// <summary>
/// PRD §3 admin-approval flow + M34 tenant-routed login. <c>/auth/centers</c> lists the centers a
/// phone belongs to (login picker); <c>/auth/login</c> authenticates against the chosen
/// <c>environmentId</c>; <c>/auth/center-by-code</c> resolves a center for self-registration;
/// <c>/auth/refresh</c> / <c>/auth/logout</c> read the env off the stored refresh token.
/// Anonymous lookups + login are IP-rate-limited ("auth" policy).
/// /auth/powersync-token (the M0 endpoint) lives in <see cref="PowerSyncTokenEndpoint"/>.
/// </summary>
public sealed class AuthModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/centers", Centers)
            .AddEndpointFilter<ValidationFilter<CentersLookupRequest>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Auth_Centers")
            .WithSummary("List the active centers a phone number belongs to (login routing).");

        group.MapPost("/center-by-code", CenterByCode)
            .AddEndpointFilter<ValidationFilter<CenterByCodeRequest>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Auth_CenterByCode")
            .WithSummary("Resolve an active center by its code (registration routing).");

        group.MapPost("/register", Register)
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Auth_Register")
            .WithSummary("Create an inactive account + pending registration request in the chosen center.");

        group.MapPost("/login", Login)
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Auth_Login")
            .WithSummary("Exchange center + phone + password for an access/refresh token pair (active accounts only).");

        group.MapPost("/refresh", Refresh)
            .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Auth_Refresh")
            .WithSummary("Rotate a refresh token; revokes the old one (env read off the token).");

        group.MapPost("/logout", Logout)
            .AddEndpointFilter<ValidationFilter<LogoutRequest>>()
            .AllowAnonymous()
            .WithName("Auth_Logout")
            .WithSummary("Revoke a refresh token server-side.");

        group.MapPost("/powersync-token", PowerSyncTokenEndpoint)
            .RequireAuthorization()
            .WithName("Auth_PowerSyncToken")
            .WithSummary("Mint a short-lived JWT for the PowerSync SDK upload/download stream.");
    }

    private static async Task<IResult> Centers(
        CentersLookupRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var centers = await auth.FindCentersForPhoneAsync(request.Phone, cancellationToken);
        return TypedResults.Ok(new CentersLookupResponse(centers));
    }

    private static async Task<IResult> CenterByCode(
        CenterByCodeRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var center = await auth.FindCenterByCodeAsync(request.Code, cancellationToken)
            ?? throw new NotFoundException("center", request.Code);
        return TypedResults.Ok(center);
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.RegisterAsync(request, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var pair = await auth.LoginAsync(request, cancellationToken);
        return TypedResults.Ok(pair);
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var pair = await auth.RefreshAsync(request.RefreshToken, cancellationToken);
        return TypedResults.Ok(pair);
    }

    private static async Task<IResult> Logout(
        LogoutRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request.RefreshToken, cancellationToken);
        return TypedResults.NoContent();
    }

    private static IResult PowerSyncTokenEndpoint(ICurrentUserAccessor user, IPowerSyncTokenService tokens)
    {
        if (user.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        // M36 — the PowerSync token must carry the tenant so Sync Rules can scope reads to one
        // environment. An env-less token (a platform super-admin's, M35) has no tenant scope and must
        // never mint a sync token — it would otherwise be a leak vector once the env predicate lands.
        if (user.EnvironmentId is not { } environmentId)
        {
            throw new ForbiddenException("environment_required", "A tenant context is required to sync.");
        }

        var result = tokens.IssueToken(userId, environmentId);
        return TypedResults.Ok(new PowerSyncTokenResponse(result.Token, result.ExpiresAt));
    }
}

public sealed record PowerSyncTokenResponse(string Token, DateTimeOffset ExpiresAt);
