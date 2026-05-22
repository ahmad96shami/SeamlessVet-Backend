using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    private readonly ICurrentUserAccessor _currentUser;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserAccessor currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<DomainEnvironment> Environments => Set<DomainEnvironment>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<SyncTestRecord> SyncTestRecords => Set<SyncTestRecord>();

    /// <summary>
    /// Read by the global env-scoped query filter on every materialization. Returns
    /// <see cref="Guid.Empty"/> when unauthenticated, which deliberately matches no row.
    /// Use <c>IgnoreQueryFilters()</c> for admin or boot-strap queries that must cross environments.
    /// </summary>
    public Guid CurrentEnvironmentId => _currentUser.EnvironmentId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        ApplyEntityConventions(modelBuilder);
    }

    private void ApplyEntityConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!clrType.IsAssignableTo(typeof(Entity)))
            {
                continue;
            }

            ConfigureEntityColumns(modelBuilder, clrType);
            ConfigureEntityIndexes(modelBuilder, clrType);
            ApplyEnvironmentScopedSoftDeleteFilter(modelBuilder, clrType);
        }
    }

    private static void ConfigureEntityColumns(ModelBuilder modelBuilder, Type clrType)
    {
        modelBuilder.Entity(clrType, builder =>
        {
            builder.Property(nameof(Entity.Id)).HasColumnName("id");
            builder.Property(nameof(Entity.EnvironmentId)).HasColumnName("environment_id");
            builder.Property(nameof(Entity.CreatedAt)).HasColumnName("created_at");
            builder.Property(nameof(Entity.UpdatedAt)).HasColumnName("updated_at");
            builder.Property(nameof(Entity.DeletedAt)).HasColumnName("deleted_at");

            builder.Property(nameof(Entity.Xmin))
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }

    private static void ConfigureEntityIndexes(ModelBuilder modelBuilder, Type clrType)
    {
        modelBuilder.Entity(clrType, builder =>
        {
            // baseline indexes per SCHEMA "Indexing baseline"
            builder.HasIndex(nameof(Entity.EnvironmentId), nameof(Entity.UpdatedAt));
            builder.HasIndex(nameof(Entity.EnvironmentId), nameof(Entity.DeletedAt));
        });
    }

    private void ApplyEnvironmentScopedSoftDeleteFilter(ModelBuilder modelBuilder, Type clrType)
    {
        // (e) => e.DeletedAt == null && e.EnvironmentId == this.CurrentEnvironmentId
        var parameter = Expression.Parameter(clrType, "e");
        var deletedAtProp = Expression.Property(parameter, nameof(Entity.DeletedAt));
        var deletedAtNull = Expression.Equal(deletedAtProp, Expression.Constant(null, typeof(DateTimeOffset?)));

        var envProp = Expression.Property(parameter, nameof(Entity.EnvironmentId));
        var contextEnv = Expression.Property(
            Expression.Constant(this),
            typeof(ApplicationDbContext).GetProperty(nameof(CurrentEnvironmentId))!);
        var envMatch = Expression.Equal(envProp, contextEnv);

        var body = Expression.AndAlso(deletedAtNull, envMatch);
        var lambda = Expression.Lambda(body, parameter);

        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
    }
}
