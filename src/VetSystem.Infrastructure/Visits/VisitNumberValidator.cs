using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Visits;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Visits;

/// <inheritdoc cref="IVisitNumberValidator"/>
public sealed partial class VisitNumberValidator : IVisitNumberValidator
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public VisitNumberValidator(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task ValidateAsync(string visitNumber, Guid? excludeVisitId, CancellationToken cancellationToken)
    {
        if (_user.UserId is not { } userId || _user.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(visitNumber) || !NumberFormat().IsMatch(visitNumber))
        {
            throw new ConflictException("invalid_visit_number",
                "visit_number must be '{prefix}-{sequence}', e.g. 'ADM-42'.");
        }

        var prefix = visitNumber[..visitNumber.IndexOf('-')];
        var ownPrefix = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.NumberPrefix)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(ownPrefix) || !string.Equals(prefix, ownPrefix, StringComparison.Ordinal))
        {
            throw new ConflictException("visit_number_prefix_mismatch",
                $"visit_number prefix '{prefix}' must match your assigned number prefix"
                + $" '{ownPrefix ?? "(none)"}'.");
        }

        // Match the ux_visits_env_number constraint exactly: it spans soft-deleted rows (no
        // deleted_at in the index), so a retired number stays reserved. IgnoreQueryFilters to see them.
        var taken = await _db.Visits
            .IgnoreQueryFilters()
            .AnyAsync(
                v => v.EnvironmentId == envId
                     && v.VisitNumber == visitNumber
                     && (excludeVisitId == null || v.Id != excludeVisitId),
                cancellationToken);

        if (taken)
        {
            throw new ConflictException("visit_number_taken",
                $"visit_number '{visitNumber}' already exists in this environment.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9]+-[0-9]+$")]
    private static partial Regex NumberFormat();
}
