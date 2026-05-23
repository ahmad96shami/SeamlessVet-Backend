namespace VetSystem.Application.Partnership;

/// <summary>
/// SCHEMA §1 partner payloads (PRD §6.8). Partners are Admin-only Center-Web data, available only in
/// a <c>partnership</c> environment — the endpoints 404 in a <c>solo</c> one.
/// </summary>
public sealed record PartnerCreateRequest(Guid? Id, Guid? UserId, string DisplayName, string? Notes);

/// <summary>Partner edit — every field optional; only supplied ones change. <c>UserId</c> cannot be cleared via PATCH.</summary>
public sealed record PartnerPatchRequest(Guid? UserId, string? DisplayName, string? Notes);

public sealed record PartnerResponse(
    Guid Id,
    Guid? UserId,
    string DisplayName,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// SCHEMA §1 partnership-share payload (PRD §6.8). A share is active on a date when
/// <c>EffectiveFrom &lt;= D &amp;&amp; (EffectiveTo is null || EffectiveTo &gt;= D)</c>. Per environment,
/// active shares may not sum to more than 100% on any date — enforced by <see cref="IPartnershipValidator"/>.
/// </summary>
public sealed record PartnershipShareCreateRequest(
    Guid? Id,
    Guid PartnerId,
    decimal SharePercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>
/// Share edit. Every field optional. Note (as elsewhere in the codebase) <c>EffectiveTo</c> can be
/// set via PATCH but not reverted to open-ended (null) — recreate the share for that.
/// </summary>
public sealed record PartnershipSharePatchRequest(
    decimal? SharePercent,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record PartnershipShareResponse(
    Guid Id,
    Guid PartnerId,
    decimal SharePercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
