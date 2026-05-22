using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Infrastructure.Persistence;

/// <summary>
/// Idempotent bootstrap seeder. Source of truth for first-run data (per <c>vet-backend/CLAUDE.md</c>).
/// M0 seeds the bootstrap environment; later milestones (M1 admin user + roles/permissions,
/// M2 system_settings, …) append their own steps inside this class.
/// </summary>
public sealed class DataSeeder
{
    public static readonly Guid BootstrapEnvironmentId = Guid.Parse("01900000-0000-7000-8000-000000000001");

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IGuidV7Generator _ids;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(ApplicationDbContext db, IClock clock, IGuidV7Generator ids, ILogger<DataSeeder> logger)
    {
        _db = db;
        _clock = clock;
        _ids = ids;
        _logger = logger;
    }

    public async Task SeedAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _db.Database.MigrateAsync(cancellationToken);

        if (force)
        {
            _logger.LogWarning("DataSeeder running with --force-seed: existing seed data will be cleared.");
            await ClearAsync(cancellationToken);
        }

        await SeedBootstrapEnvironmentAsync(cancellationToken);
    }

    private async Task SeedBootstrapEnvironmentAsync(CancellationToken cancellationToken)
    {
        var exists = await _db.Environments
            .IgnoreQueryFilters()
            .AnyAsync(e => e.Id == BootstrapEnvironmentId, cancellationToken);

        if (exists)
        {
            return;
        }

        var now = _clock.UtcNow;
        _db.Environments.Add(new DomainEnvironment
        {
            Id = BootstrapEnvironmentId,
            Name = "Bootstrap",
            Mode = "solo",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded bootstrap environment {EnvironmentId}", BootstrapEnvironmentId);
    }

    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE idempotency_keys, sync_test_records, environments RESTART IDENTITY CASCADE;",
            cancellationToken);
    }
}
