namespace VetSystem.Application.Identity.Contracts;

public sealed record RegisterRequest(
    string FullName,
    string PhonePrimary,
    string? Email,
    string Password,
    string RequestedRoleKey,
    string? LicenseNumber,
    string? LicenseDetails);

public sealed record RegisterResponse(Guid UserId, Guid RegistrationRequestId);

public sealed record LoginRequest(string PhonePrimary, string Password);

/// <summary>
/// Token pair returned by /auth/login and /auth/refresh. <see cref="NumberPrefix"/> is the
/// admin-assigned per-environment prefix used to mint per-user `{prefix}-{seq}` visit and
/// invoice numbers client-side (PRD §6.2; mobile mints `visit_number` for the field
/// doctor's offline visits — Mo2). Null when no prefix is assigned (admin/accountant
/// roles never get one).
/// </summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string RoleKey,
    string? NumberPrefix);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ApproveRequest(string? Notes);

public sealed record RejectRequest(string Notes);

public sealed record PermissionOverrideRequest(string PermissionKey, string Effect);

public sealed record RegistrationRequestSummary(
    Guid Id,
    Guid UserId,
    string FullName,
    string PhonePrimary,
    string? Email,
    string RequestedRoleKey,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>A row in the admin user roster (GET /admin/users). Never exposes the password hash.</summary>
public sealed record UserResponse(
    Guid Id,
    string FullName,
    string PhonePrimary,
    string? Email,
    string RoleKey,
    string RoleName,
    string Status,
    string? NumberPrefix,
    string? LicenseNumber,
    DateTimeOffset CreatedAt);

public sealed record UserPermissionOverrideItem(string PermissionKey, string Effect);

/// <summary>Single user (GET /admin/users/{id}) + their permission overrides (for the override editor).</summary>
public sealed record UserDetailResponse(
    Guid Id,
    string FullName,
    string PhonePrimary,
    string? Email,
    string RoleKey,
    string RoleName,
    string Status,
    string? NumberPrefix,
    string? LicenseNumber,
    string? LicenseDetails,
    DateTimeOffset CreatedAt,
    IReadOnlyList<UserPermissionOverrideItem> PermissionOverrides);
