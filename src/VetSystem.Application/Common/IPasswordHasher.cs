namespace VetSystem.Application.Common;

/// <summary>
/// Server-side password hashing. Implementation lives in Infrastructure (BCrypt) so the
/// Application layer never references the algorithm directly. PRD §3 mandates BCrypt + a
/// custom auth path (no ASP.NET Identity).
/// </summary>
public interface IPasswordHasher
{
    string Hash(string plaintext);

    bool Verify(string plaintext, string hash);
}
