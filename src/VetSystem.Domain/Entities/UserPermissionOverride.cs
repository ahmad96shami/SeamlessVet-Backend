using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>SCHEMA §1 — grant or deny override on top of the role default. Unique per (user, permission).</summary>
public sealed class UserPermissionOverride : Entity
{
    public Guid UserId { get; set; }

    public Guid PermissionId { get; set; }

    public string Effect { get; set; } = OverrideEffect.Grant;
}

public static class OverrideEffect
{
    public const string Grant = "grant";
    public const string Deny = "deny";

    public static readonly IReadOnlyCollection<string> All = [Grant, Deny];
}
