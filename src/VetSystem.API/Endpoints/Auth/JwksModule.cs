using VetSystem.API.Identity;

namespace VetSystem.API.Endpoints.Auth;

/// <summary>
/// Serves the public half of the PowerSync signing key as a JWKS document. The PowerSync
/// Service points <c>client_auth.jwks_uri</c> at this endpoint to validate the JWTs minted by
/// <c>POST /auth/powersync-token</c>.
/// </summary>
public sealed class JwksModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/jwks.json", (IPowerSyncTokenService tokens) =>
            TypedResults.Ok(new { keys = tokens.GetJwks() }))
            .AllowAnonymous()
            .WithName("Auth_Jwks")
            .WithTags("Auth")
            .WithSummary("JWKS document for PowerSync token verification.");
    }
}
