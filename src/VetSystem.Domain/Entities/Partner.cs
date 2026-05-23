using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §1 — a financial partner in a <c>partnership</c> environment (PRD §6.8). Partners exist
/// only to receive a configurable share of the clinic's net profit; a <c>solo</c> environment has
/// none, and the partner endpoints 404 there. Optionally linked to a login <see cref="UserId"/> when
/// the partner is also a staff account, but the link is not required (a silent investor has no login).
/// Admin-only, Center-Web data — not doctor-scoped, so it is never mirrored to a field device.
/// </summary>
public sealed class Partner : Entity
{
    /// <summary>Optional link to a login account; null for a partner who is not a system user.</summary>
    public Guid? UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
