namespace VetSystem.Domain.Entities;

/// <summary>
/// M35 — a platform super-administrator who lives <em>outside</em> any tenant. Provisions, lists,
/// suspends, and reactivates centers via the platform console; never reads or writes tenant data.
///
/// Deliberately a plain POCO like <see cref="DeviceToken"/> / <see cref="IdempotencyKey"/>, NOT an
/// <see cref="Common.Entity"/>: there is no <c>environment_id</c> (the platform admin is not
/// tenant-scoped), so the shared conventions — env-scoped query filter, audit stamping, the
/// immutable-env guard, soft-delete conversion — are all wrong here. Its <c>phone</c> is globally
/// unique. Excluded from <c>DataSeeder.ClearAsync</c>'s TRUNCATE so it survives <c>--force-seed</c>.
/// </summary>
public sealed class PlatformAdmin
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Globally-unique login identifier (no tenant routing — the platform has one realm).</summary>
    public string Phone { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Operational state. A <c>suspended</c> platform admin can no longer authenticate at
    /// <c>/platform/auth/login</c> (the row is kept for audit rather than hard-deleted).
    /// </summary>
    public string Status { get; set; } = PlatformAdminStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class PlatformAdminStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";

    public static readonly IReadOnlyCollection<string> All = [Active, Suspended];
}
