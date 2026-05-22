namespace VetSystem.Domain.Common;

/// <summary>
/// Base for every syncable entity. Standard columns per <c>docs/SCHEMA.md</c>:
/// Guid v7 PK generated client-side, environment tenant key, timestamps,
/// soft-delete flag, and <c>xmin</c> mapped as the EF optimistic concurrency token.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; }

    public Guid EnvironmentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Postgres system column <c>xmin</c>, mapped via EF as the concurrency token.</summary>
    public uint Xmin { get; set; }

    public bool IsDeleted => DeletedAt is not null;
}
