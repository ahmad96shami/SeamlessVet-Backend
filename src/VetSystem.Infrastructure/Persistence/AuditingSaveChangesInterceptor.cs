using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;

namespace VetSystem.Infrastructure.Persistence;

/// <summary>
/// Stamps audit columns and assigns a Guid v7 id when the client did not supply one.
/// SCHEMA "Standard columns": id, environment_id, created_at, updated_at are set here so
/// individual services do not repeat the work.
/// </summary>
public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;
    private readonly IGuidV7Generator _ids;
    private readonly ICurrentUserAccessor _currentUser;

    public AuditingSaveChangesInterceptor(IClock clock, IGuidV7Generator ids, ICurrentUserAccessor currentUser)
    {
        _clock = clock;
        _ids = ids;
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        var envFromUser = _currentUser.EnvironmentId;

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                    {
                        entry.Entity.Id = _ids.New();
                    }

                    if (entry.Entity.EnvironmentId == Guid.Empty && envFromUser is { } env)
                    {
                        entry.Entity.EnvironmentId = env;
                    }

                    // Hard isolation guard (M10 task 7): every syncable row must be stamped with an
                    // environment before it is written. If neither the row nor the current user supplied
                    // one, fail fast here rather than letting a NULL hit the database — a row with no
                    // environment is invisible to the env-scoped query filter and would orphan/leak.
                    if (entry.Entity.EnvironmentId == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            $"EnvironmentId was not set on {entry.Entity.GetType().Name} before save and could "
                            + "not be resolved from the current user; every syncable entity must be environment-scoped.");
                    }

                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    PreventEnvironmentChange(entry);
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted:
                    // Soft delete: convert to Modified and stamp DeletedAt.
                    entry.State = EntityState.Modified;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }

    private static void PreventEnvironmentChange(EntityEntry<Entity> entry)
    {
        var envProp = entry.Property(e => e.EnvironmentId);
        if (envProp.IsModified && envProp.OriginalValue != envProp.CurrentValue)
        {
            throw new InvalidOperationException(
                "EnvironmentId is immutable on a syncable entity; cross-environment moves are not supported.");
        }
    }
}
