using Microsoft.IdentityModel.Tokens;

namespace VetSystem.API.Identity;

public interface IPowerSyncTokenService
{
    /// <summary>
    /// Returns a short-lived JWT carrying the user (in <c>sub</c>, resolved by <c>auth.user_id()</c>)
    /// and the tenant (M36 — <c>environment_id</c> both top-level and under a <c>parameters</c> claim,
    /// resolved by <c>auth.parameters() -&gt;&gt; 'environment_id'</c>), signed by the PowerSync signing
    /// key. The PowerSync Service trusts this API's JWKS endpoint and uses the env claim to scope every
    /// Sync-Stream query to one tenant — see <c>powersync/sync-rules.yaml</c>.
    /// </summary>
    PowerSyncTokenResult IssueToken(Guid userId, Guid environmentId);

    /// <summary>
    /// Returns the public signing material as a JSON Web Key (RFC 7517) — served at
    /// <c>/.well-known/jwks.json</c> and consumed by the PowerSync Service.
    /// </summary>
    IReadOnlyList<JsonWebKey> GetJwks();
}

public sealed record PowerSyncTokenResult(string Token, DateTimeOffset ExpiresAt);
