using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Visits;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Visits;

/// <inheritdoc cref="IVisitNumberGenerator"/>
public sealed partial class VisitNumberGenerator : IVisitNumberGenerator
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public VisitNumberGenerator(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<string?> NextForCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (_user.UserId is not { } userId || _user.EnvironmentId is not { } envId) return null;

        var prefix = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.NumberPrefix)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(prefix)) return null;

        var likePattern = prefix + "-%";
        // Include soft-deleted rows so a retired sequence stays reserved (mirrors the unique-index
        // shape — index has no deleted_at predicate). Scan only this env + this prefix's bucket.
        var existing = await _db.Visits
            .IgnoreQueryFilters()
            .Where(v => v.EnvironmentId == envId
                        && v.VisitNumber != null
                        && EF.Functions.Like(v.VisitNumber!, likePattern))
            .Select(v => v.VisitNumber!)
            .ToListAsync(cancellationToken);

        var nextSeq = 1;
        foreach (var n in existing)
        {
            var m = SeqMatcher().Match(n);
            if (!m.Success) continue;
            if (int.TryParse(m.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
                && seq >= nextSeq)
            {
                nextSeq = seq + 1;
            }
        }

        return $"{prefix}-{nextSeq.ToString(CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex("-(\\d+)$")]
    private static partial Regex SeqMatcher();
}
