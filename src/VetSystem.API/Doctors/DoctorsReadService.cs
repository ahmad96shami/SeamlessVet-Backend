using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Doctors;

/// <summary>
/// Read-only roster of the environment's veterinarians for the visit / appointment / follow-up
/// doctor pickers. Authenticated-only (the front desk, cashiers, and vets all assign a doctor but
/// hold no <c>users.manage</c> permission, so <c>/admin/users</c> is off-limits to them), and
/// environment-scoped by the global query filter. Replaces the earlier stopgap where the web sourced
/// the doctor list from <c>/inventory/field-inventories</c> — which only ever surfaced field doctors,
/// so clinic vets could never be picked for an in-clinic visit.
/// </summary>
public sealed class DoctorsReadService
{
    private static readonly string[] VetRoleKeys = [RoleKey.VetClinic, RoleKey.VetField, RoleKey.VetBoth];

    private readonly ApplicationDbContext _db;

    public DoctorsReadService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /doctors — active users in a vet role (clinic / field / both), ordered by name.
    /// </summary>
    public async Task<IReadOnlyList<DoctorResponse>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.Users
            .AsNoTracking()
            .Join(_db.Roles, u => u.RoleId, r => r.Id, (u, r) => new { u, r })
            .Where(x => x.u.Status == UserStatus.Active && VetRoleKeys.Contains(x.r.Key))
            .OrderBy(x => x.u.FullName)
            .Select(x => new DoctorResponse(x.u.Id, x.u.FullName, x.r.Key))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>A pickable veterinarian: <see cref="Id"/>, display <see cref="Name"/>, and role key.</summary>
public sealed record DoctorResponse(Guid Id, string Name, string Role);
