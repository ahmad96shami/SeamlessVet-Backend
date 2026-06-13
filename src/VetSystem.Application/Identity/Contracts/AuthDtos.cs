namespace VetSystem.Application.Identity.Contracts;

public sealed record RegisterRequest(
    Guid EnvironmentId,
    string FullName,
    string PhonePrimary,
    string? Email,
    string Password,
    string RequestedRoleKey,
    string? LicenseNumber,
    string? LicenseDetails);

public sealed record RegisterResponse(Guid UserId, Guid RegistrationRequestId);

/// <summary>
/// M34 — login is tenant-routed: the client first calls <c>/auth/centers</c> with the phone, picks
/// the center, then logs in with that <see cref="EnvironmentId"/> + phone + password.
/// </summary>
public sealed record LoginRequest(Guid EnvironmentId, string PhonePrimary, string Password);

/// <summary>POST /auth/centers — list the centers a phone belongs to (login routing).</summary>
public sealed record CentersLookupRequest(string Phone);

/// <summary>POST /auth/center-by-code — resolve a center by its human code (registration routing).</summary>
public sealed record CenterByCodeRequest(string Code);

/// <summary>A center a user may sign into (minimal, no sensitive data).</summary>
public sealed record CenterOption(Guid EnvironmentId, string Name, string Code);

public sealed record CentersLookupResponse(IReadOnlyList<CenterOption> Centers);

/// <summary>
/// Token pair returned by /auth/login and /auth/refresh. <see cref="NumberPrefix"/> is the
/// admin-assigned per-environment prefix used to mint per-user `{prefix}-{seq}` visit and
/// invoice numbers client-side (PRD §6.2; mobile mints `visit_number` for the field
/// doctor's offline visits — Mo2). Null when no prefix is assigned (admin/accountant
/// roles never get one). <see cref="FullName"/> powers the clients' greeting / profile
/// header (MoD) — the JWT itself carries no name claim.
/// </summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string RoleKey,
    string FullName,
    string? NumberPrefix);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ApproveRequest(string? Notes);

public sealed record RejectRequest(string Notes);

public sealed record PermissionOverrideRequest(string PermissionKey, string Effect);

/// <summary>
/// POST /admin/users body — an admin-created staff account (cashier, in-clinic doctor, …) that
/// skips the self-registration approval queue and is active immediately.
/// </summary>
public sealed record CreateUserRequest(
    string FullName,
    string PhonePrimary,
    string? Email,
    string Password,
    string RoleKey,
    string? LicenseNumber,
    string? LicenseDetails);

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
