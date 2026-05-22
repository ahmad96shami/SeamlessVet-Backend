using Microsoft.IdentityModel.Tokens;

namespace VetSystem.API.Identity;

public interface IPowerSyncTokenService
{
    /// <summary>
    /// Returns a short-lived JWT carrying <c>user_id</c> in <c>token_parameters</c>, signed by the
    /// PowerSync signing key. The PowerSync Service trusts this API's JWKS endpoint and resolves
    /// <c>token_parameters.user_id</c> in Sync Rules per <c>docs/TECH_STACK.md</c>.
    /// </summary>
    PowerSyncTokenResult IssueToken(Guid userId);

    /// <summary>
    /// Returns the public signing material as a JSON Web Key (RFC 7517) — served at
    /// <c>/.well-known/jwks.json</c> and consumed by the PowerSync Service.
    /// </summary>
    IReadOnlyList<JsonWebKey> GetJwks();
}

public sealed record PowerSyncTokenResult(string Token, DateTimeOffset ExpiresAt);
