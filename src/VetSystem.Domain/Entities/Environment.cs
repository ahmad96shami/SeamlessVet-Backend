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

    /// <summary>
    /// Short, human-friendly, globally-unique tenant code (e.g. <c>RAMALLAH-VET</c>) used by the
    /// platform console for display/support. Not used for login routing (that is phone-number
    /// lookup) but a stable identifier a support operator can quote.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Mode { get; set; } = EnvironmentMode.Solo;

    /// <summary>
    /// Operational state. A <c>suspended</c> center refuses tenant logins and the per-request
    /// <c>EnvironmentStatusMiddleware</c> rejects its already-issued tokens with
    /// <c>environment_suspended</c>. Only the platform console flips this.
    /// </summary>
    public string Status { get; set; } = EnvironmentStatus.Active;

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

public static class EnvironmentStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";

    public static readonly IReadOnlyCollection<string> All = [Active, Suspended];
}
