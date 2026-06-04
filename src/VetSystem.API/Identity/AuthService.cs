using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VetSystem.Application.Common;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Identity;

/// <summary>
/// Orchestrates register / login / refresh / logout for the auth endpoints. Every write here
/// runs inside the env-scoped DbContext but uses <c>IgnoreQueryFilters()</c> where the operator
/// is the user themselves (unauthenticated register / login) so the filter doesn't reject the row.
/// </summary>
public sealed class AuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _tokens;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IClock _clock;
    private readonly JwtOptions _jwt;

    public AuthService(
        ApplicationDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService tokens,
        IRefreshTokenStore refreshStore,
        IClock clock,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _refreshStore = refreshStore;
        _clock = clock;
        _jwt = jwtOptions.Value;
    }

    public async Task<RegisterResponse> RegisterAsync(
        Guid environmentId,
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!RoleKey.All.Contains(request.RequestedRoleKey))
        {
            throw new ConflictException("invalid_role", $"Role '{request.RequestedRoleKey}' is not a valid role key.");
        }

        var role = await _db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.EnvironmentId == environmentId
                     && r.Key == request.RequestedRoleKey
                     && r.DeletedAt == null,
                cancellationToken)
            ?? throw new ConflictException(
                "role_not_seeded",
                $"Role '{request.RequestedRoleKey}' is not seeded in environment {environmentId}.");

        var phoneInUse = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(
                u => u.EnvironmentId == environmentId
                     && u.PhonePrimary == request.PhonePrimary
                     && u.DeletedAt == null,
                cancellationToken);

        if (phoneInUse)
        {
            throw new ConflictException("phone_in_use", "An account with this phone number already exists.");
        }

        var user = new User
        {
            EnvironmentId = environmentId,
            RoleId = role.Id,
            FullName = request.FullName.Trim(),
            PhonePrimary = request.PhonePrimary.Trim(),
            Email = request.Email?.Trim(),
            PasswordHash = _hasher.Hash(request.Password),
            Status = UserStatus.Inactive,
            LicenseNumber = request.LicenseNumber?.Trim(),
            LicenseDetails = request.LicenseDetails,
        };

        var registration = new RegistrationRequest
        {
            EnvironmentId = environmentId,
            UserId = user.Id, // 0 here; auditing interceptor stamps Id, then EF resolves the FK on save.
            RequestedRoleKey = request.RequestedRoleKey,
            Status = RequestStatus.Pending,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        registration.UserId = user.Id;
        _db.RegistrationRequests.Add(registration);
        await _db.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(user.Id, registration.Id);
    }

    public async Task<TokenPair> LoginAsync(
        Guid environmentId,
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.EnvironmentId == environmentId
                        && u.PhonePrimary == request.PhonePrimary
                        && u.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            // Deliberately uniform error: don't leak phone-existence signal.
            throw new ForbiddenException("invalid_credentials", "Phone or password is incorrect.");
        }

        if (user.Status == UserStatus.Inactive)
        {
            throw new ForbiddenException(
                "account_inactive",
                "This account is still pending administrator approval.");
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw new ForbiddenException("account_suspended", "This account is suspended.");
        }

        return await IssueTokenPairAsync(user, cancellationToken);
    }

    public async Task<TokenPair> RefreshAsync(
        Guid environmentId,
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        var existing = await _refreshStore.FindActiveAsync(rawRefreshToken, environmentId, cancellationToken)
                       ?? throw new ForbiddenException("invalid_refresh_token", "Refresh token is invalid or expired.");

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.Id == existing.UserId
                     && u.EnvironmentId == environmentId
                     && u.DeletedAt == null,
                cancellationToken)
            ?? throw new ForbiddenException("invalid_refresh_token", "Refresh token is invalid or expired.");

        if (user.Status != UserStatus.Active)
        {
            await _refreshStore.RevokeAsync(existing, "user_not_active", cancellationToken);
            throw new ForbiddenException("account_not_active", "Account is not active.");
        }

        var roleKey = await GetRoleKeyAsync(user.RoleId, cancellationToken);

        var newRaw = _tokens.IssueRefreshTokenValue();
        var rotated = await _refreshStore.RotateAsync(
            existing,
            newRaw,
            TimeSpan.FromDays(_jwt.RefreshTokenDays),
            cancellationToken);

        var access = _tokens.IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, roleKey));

        return new TokenPair(
            access.Token,
            access.ExpiresAt,
            newRaw,
            rotated.ExpiresAt,
            user.Id,
            roleKey,
            user.FullName,
            user.NumberPrefix);
    }

    public async Task LogoutAsync(Guid environmentId, string rawRefreshToken, CancellationToken cancellationToken)
    {
        var existing = await _refreshStore.FindActiveAsync(rawRefreshToken, environmentId, cancellationToken);
        if (existing is null)
        {
            return; // logout is best-effort; an unknown/expired token is a no-op.
        }

        await _refreshStore.RevokeAsync(existing, "logout", cancellationToken);
    }

    private async Task<TokenPair> IssueTokenPairAsync(User user, CancellationToken cancellationToken)
    {
        var roleKey = await GetRoleKeyAsync(user.RoleId, cancellationToken);

        var access = _tokens.IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, roleKey));
        var rawRefresh = _tokens.IssueRefreshTokenValue();
        var stored = await _refreshStore.IssueAsync(
            user.Id,
            user.EnvironmentId,
            rawRefresh,
            TimeSpan.FromDays(_jwt.RefreshTokenDays),
            cancellationToken);

        return new TokenPair(
            access.Token,
            access.ExpiresAt,
            rawRefresh,
            stored.ExpiresAt,
            user.Id,
            roleKey,
            user.FullName,
            user.NumberPrefix);
    }

    private Task<string> GetRoleKeyAsync(Guid roleId, CancellationToken cancellationToken)
        => _db.Roles
            .IgnoreQueryFilters()
            .Where(r => r.Id == roleId)
            .Select(r => r.Key)
            .FirstAsync(cancellationToken);
}
