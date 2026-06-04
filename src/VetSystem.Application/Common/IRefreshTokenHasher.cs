namespace VetSystem.Application.Common;

/// <summary>
/// Deterministic hashing for refresh tokens (Infrastructure supplies SHA-256). Refresh tokens are
/// 256-bit CSPRNG values, so a salted slow hash adds nothing — brute force is already infeasible —
/// while determinism lets the store match on an indexed <c>WHERE token_hash = …</c> instead of
/// BCrypt-verifying every active row (which grew O(n) and hung /auth/refresh as logins accumulated).
/// Passwords keep <see cref="IPasswordHasher"/>; this is for high-entropy machine secrets only.
/// </summary>
public interface IRefreshTokenHasher
{
    string Hash(string rawToken);
}
