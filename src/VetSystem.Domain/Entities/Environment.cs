using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §1 — the tenancy boundary. Not itself an <see cref="Entity"/> because the
/// environments table has no <c>environment_id</c> column (it IS the environments table).
/// </summary>
public sealed class Environment
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Mode { get; set; } = EnvironmentMode.Solo;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public uint Xmin { get; set; }
}

public static class EnvironmentMode
{
    public const string Solo = "solo";
    public const string Partnership = "partnership";

    public static readonly IReadOnlyCollection<string> All = [Solo, Partnership];
}
