using VetSystem.API.Filters;
using VetSystem.API.Identity;
using VetSystem.Application.Common;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Auth;

/// <summary>
/// PRD §3 admin-approval flow: /auth/register creates an inactive user + pending request;
/// /auth/login refuses anything that isn't <c>users.status = 'active'</c>;
/// /auth/refresh rotates server-side refresh tokens; /auth/logout revokes.
/// /auth/powersync-token (the M0 endpoint) lives in <see cref="PowerSyncTokenEndpoint"/>.
/// </summary>
public sealed class AuthModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
            .AllowAnonymous()
            .WithName("Auth_Register")
            .WithSummary("Create an inactive account + pending registration request.");

        group.MapPost("/login", Login)
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .AllowAnonymous()
            .WithName("Auth_Login")
            .WithSummary("Exchange phone + password for an access/refresh token pair (active accounts only).");

        group.MapPost("/refresh", Refresh)
            .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
            .AllowAnonymous()
            .WithName("Auth_Refresh")
            .WithSummary("Rotate a refresh token; revokes the old one.");

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

    private static async Task<IResult> Register(
        RegisterRequest request,
        AuthService auth,
        IRequestEnvironmentResolver envResolver,
        CancellationToken cancellationToken)
    {
        var environmentId = envResolver.Resolve();
        var result = await auth.RegisterAsync(environmentId, request, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AuthService auth,
        IRequestEnvironmentResolver envResolver,
        CancellationToken cancellationToken)
    {
        var environmentId = envResolver.Resolve();
        var pair = await auth.LoginAsync(environmentId, request, cancellationToken);
        return TypedResults.Ok(pair);
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        AuthService auth,
        IRequestEnvironmentResolver envResolver,
        CancellationToken cancellationToken)
    {
        var environmentId = envResolver.Resolve();
        var pair = await auth.RefreshAsync(environmentId, request.RefreshToken, cancellationToken);
        return TypedResults.Ok(pair);
    }

    private static async Task<IResult> Logout(
        LogoutRequest request,
        AuthService auth,
        IRequestEnvironmentResolver envResolver,
        CancellationToken cancellationToken)
    {
        var environmentId = envResolver.Resolve();
        await auth.LogoutAsync(environmentId, request.RefreshToken, cancellationToken);
        return TypedResults.NoContent();
    }

    private static IResult PowerSyncTokenEndpoint(ICurrentUserAccessor user, IPowerSyncTokenService tokens)
    {
        if (user.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var result = tokens.IssueToken(userId);
        return TypedResults.Ok(new PowerSyncTokenResponse(result.Token, result.ExpiresAt));
    }
}

public sealed record PowerSyncTokenResponse(string Token, DateTimeOffset ExpiresAt);
