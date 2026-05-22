using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Identity;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Identity;

/// <summary>
/// 3-letter A-Z prefix; retries on collision; widens to 4 letters after 200 failed attempts so
/// large environments don't deadlock the namespace. UNIQUE constraint
/// <c>ux_users_env_number_prefix</c> backs the no-collision guarantee.
/// </summary>
public sealed class NumberPrefixGenerator : INumberPrefixGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int MaxAttemptsAtBaseLength = 200;
    private const int BaseLength = 3;
    private const int WidenedLength = 4;

    private readonly ApplicationDbContext _db;

    public NumberPrefixGenerator(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string> GenerateUniqueAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttemptsAtBaseLength; attempt++)
        {
            var candidate = Random(BaseLength);
            if (!await ExistsAsync(environmentId, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        // Namespace tightening: widen.
        for (var attempt = 0; attempt < MaxAttemptsAtBaseLength; attempt++)
        {
            var candidate = Random(WidenedLength);
            if (!await ExistsAsync(environmentId, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new ConflictException(
            "number_prefix_exhausted",
            $"Could not allocate a unique number_prefix in environment {environmentId} after extensive retries.");
    }

    private Task<bool> ExistsAsync(Guid environmentId, string candidate, CancellationToken cancellationToken)
        => _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EnvironmentId == environmentId && u.NumberPrefix == candidate, cancellationToken);

    private static string Random(int length)
    {
        Span<char> buffer = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }
}
