namespace VetSystem.Application.Platform.Contracts;

/// <summary>POST /platform/auth/login — the platform realm has one login (no tenant routing).</summary>
public sealed record PlatformLoginRequest(string Phone, string Password);

/// <summary>
/// The platform-admin access token (no refresh in v1). <see cref="FullName"/> powers the console
/// header; the JWT itself carries no name claim.
/// </summary>
public sealed record PlatformAuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    Guid PlatformAdminId,
    string FullName);

/// <summary>A center as the platform console sees it (GET /platform/tenants[/{id}]).</summary>
public sealed record TenantSummary(
    Guid Id,
    string Name,
    string Code,
    string Mode,
    string Status,
    int UserCount,
    DateTimeOffset CreatedAt);

public sealed record TenantListResponse(IReadOnlyList<TenantSummary> Tenants);
