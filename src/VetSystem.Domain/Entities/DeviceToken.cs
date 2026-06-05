namespace VetSystem.Domain.Entities;

/// <summary>
/// M21 — a mobile device's Expo push token, registered self-scoped via <c>/devices/push-token</c>
/// so the push fan-out can reach a backgrounded/killed app.
///
/// Deliberately a plain POCO like <see cref="IdempotencyKey"/>, NOT an <see cref="Common.Entity"/>:
/// the token is globally unique (one physical device = one row) and a shared device re-registers
/// under whichever user signs in — the base class's immutable-EnvironmentId guard would throw on
/// that reassignment, its soft-delete conversion would leave dead rows colliding with the unique
/// token index, and the env-scoped query filter is wrong for the no-principal push worker.
/// Server-only (never synced, hard-deleted on unregister/prune).
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid EnvironmentId { get; set; }

    /// <summary>The Expo push token (e.g. <c>ExponentPushToken[…]</c>); globally unique.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>"android" | "ios" (check-constrained).</summary>
    public string Platform { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Bumped on every (re-)registration — lets ops spot tokens gone quiet.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
