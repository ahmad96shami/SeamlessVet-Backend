using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Partnership;

/// <summary>
/// Partnership endpoints exist only in a <c>partnership</c> environment (PRD §6.8, task 2). In a
/// <c>solo</c> environment the feature is absent, so the endpoints behave as if the route does not
/// exist — a 404 — rather than leaking that partnership data structures are present but empty.
/// </summary>
internal static class PartnershipGuard
{
    public static async Task EnsurePartnershipEnvironmentAsync(
        ApplicationDbContext db, Guid environmentId, CancellationToken cancellationToken)
    {
        var mode = await db.Environments
            .Where(e => e.Id == environmentId)
            .Select(e => e.Mode)
            .FirstOrDefaultAsync(cancellationToken);

        if (mode != EnvironmentMode.Partnership)
        {
            throw new NotFoundException("partnership", environmentId);
        }
    }
}
