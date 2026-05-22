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

public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string RoleKey);

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
