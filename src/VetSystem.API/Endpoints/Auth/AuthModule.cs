using VetSystem.API.Identity;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Auth;

/// <summary>
/// M0 ships only the PowerSync token mint — the SDK's <c>fetchCredentials</c> calls this from
/// the upload connector. Full register / login / refresh / logout land in M1.
/// </summary>
public sealed class AuthModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/powersync-token", PowerSyncToken)
            .RequireAuthorization()
            .WithName("Auth_PowerSyncToken")
            .WithSummary("Mint a short-lived JWT for the PowerSync SDK upload/download stream.");
    }

    private static IResult PowerSyncToken(ICurrentUserAccessor user, IPowerSyncTokenService tokens)
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
