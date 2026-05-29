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
            // Project to a minimal RFC 7517 JWK. Microsoft's JsonWebKey serializes its empty
            // collections (key_ops, oth, x5c) as `[]`; a *present* `oth` makes strict verifiers
            // — notably `jose`, used by the PowerSync Service — reject the key as a multi-prime
            // RSA key, leaving the keystore empty and every stream token failing PSYNC_S2101.
            // Emit only the members RS256 verification needs.
            TypedResults.Ok(new
            {
                keys = tokens.GetJwks().Select(k => new
                {
                    kty = k.Kty,
                    use = k.Use,
                    alg = k.Alg,
                    kid = k.Kid,
                    n = k.N,
                    e = k.E,
                }),
            }))
            .AllowAnonymous()
            .WithName("Auth_Jwks")
            .WithTags("Auth")
            .WithSummary("JWKS document for PowerSync token verification.");
    }
}
