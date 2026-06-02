using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Infrastructure.Persistence;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Tests.Infrastructure;

/// <summary>
/// Postgres-backed test scope. Reuses the dev <c>docker compose</c> Postgres (CLAUDE.md says
/// integration tests run against a PG test container — M13 will swap to Testcontainers). Each
/// scope owns one <see cref="EnvironmentId"/> so concurrent tests never collide on env-scoped
/// uniqueness or query filters. Disposing the scope cascades-deletes everything in the env.
/// </summary>
public sealed class PgTestScope : IAsyncDisposable
{
    public const string ConnectionString =
        "Host=localhost;Port=5433;Database=vet;Username=vet;Password=vet_dev_password";

    public Guid EnvironmentId { get; }

    private PgTestScope(Guid environmentId)
    {
        EnvironmentId = environmentId;
    }

    public static async Task<PgTestScope> CreateAsync(string mode = "solo")
    {
        var envId = Guid.CreateVersion7();
        var scope = new PgTestScope(envId);

        await using var db = scope.CreateDbContext(new FakeCurrentUser());
        db.Environments.Add(new DomainEnvironment
        {
            Id = envId,
            Name = $"Test-{envId:N}".Substring(0, 32),
            Mode = mode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return scope;
    }

    public ApplicationDbContext CreateDbContext(ICurrentUserAccessor user)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options, user);
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = CreateDbContext(new FakeCurrentUser());
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM environments WHERE id = {EnvironmentId};");
    }
}
