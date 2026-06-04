using System.Security.Cryptography;
using System.Text;
using VetSystem.Application.Common;

namespace VetSystem.Infrastructure.Identity;

/// <summary>
/// SHA-256 over the raw token, hex-encoded. Deterministic so <c>ix_refresh_tokens_hash</c> serves
/// the lookup; see <see cref="IRefreshTokenHasher"/> for why this is safe for refresh tokens.
/// </summary>
public sealed class Sha256RefreshTokenHasher : IRefreshTokenHasher
{
    public string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
