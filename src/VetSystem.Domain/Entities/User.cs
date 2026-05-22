using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §1 — one account works across both clients (PRD §3). Inactive until an admin approves
/// the matching <see cref="RegistrationRequest"/>; once approved, <see cref="NumberPrefix"/> is set
/// and unique per environment so offline-generated visit/invoice numbers never collide
/// (SCHEMA "Key invariants" #9).
/// </summary>
public sealed class User : Entity
{
    public Guid RoleId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhonePrimary { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public string Status { get; set; } = UserStatus.Inactive;

    public string? NumberPrefix { get; set; }

    public string? LicenseNumber { get; set; }

    public string? LicenseDetails { get; set; }
}

public static class UserStatus
{
    public const string Inactive = "inactive";
    public const string Active = "active";
    public const string Suspended = "suspended";

    public static readonly IReadOnlyCollection<string> All = [Inactive, Active, Suspended];
}
