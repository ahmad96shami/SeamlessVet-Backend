using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Platform.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Identity;

/// <summary>
/// M35 — authenticates a platform super-admin at <c>/platform/auth/login</c>. The platform realm is
/// global (no tenant routing): a single globally-unique phone, no <c>environment_id</c>. Issues a
/// <c>platform_admin</c> access token; there is no refresh flow in v1.
/// </summary>
public sealed class PlatformAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IPlatformTokenService _tokens;

    public PlatformAuthService(
        ApplicationDbContext db,
        IPasswordHasher hasher,
        IPlatformTokenService tokens)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
    }

    public async Task<PlatformAuthResponse> LoginAsync(
        PlatformLoginRequest request,
        CancellationToken cancellationToken)
    {
        // platform_admins is a plain POCO with no env query filter, so a straight lookup is correct.
        var admin = await _db.PlatformAdmins
            .FirstOrDefaultAsync(p => p.Phone == request.Phone.Trim(), cancellationToken);

        if (admin is null || !_hasher.Verify(request.Password, admin.PasswordHash))
        {
            // Uniform error: don't leak whether the phone is a registered platform admin.
            throw new ForbiddenException("invalid_credentials", "Phone or password is incorrect.");
        }

        if (admin.Status != PlatformAdminStatus.Active)
        {
            throw new ForbiddenException("platform_account_suspended", "This platform account is suspended.");
        }

        var token = _tokens.IssuePlatformToken(new PlatformPrincipal(admin.Id));
        return new PlatformAuthResponse(token.Token, token.ExpiresAt, admin.Id, admin.FullName);
    }
}
