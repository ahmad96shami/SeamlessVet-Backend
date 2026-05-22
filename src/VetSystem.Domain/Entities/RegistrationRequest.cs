using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>SCHEMA §1 — admin-approval workflow. Mirrors <see cref="User.Status"/> until reviewed.</summary>
public sealed class RegistrationRequest : Entity
{
    public Guid UserId { get; set; }

    public string RequestedRoleKey { get; set; } = string.Empty;

    public string Status { get; set; } = RequestStatus.Pending;

    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewNotes { get; set; }
}

public static class RequestStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly IReadOnlyCollection<string> All = [Pending, Approved, Rejected];
}
