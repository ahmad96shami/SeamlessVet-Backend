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
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var environmentId = request.EnvironmentId;
        await EnsureEnvironmentActiveAsync(environmentId, cancellationToken);

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
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var environmentId = request.EnvironmentId;
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

        // Reject login into a suspended/deleted center even if the client posts its id directly
        // (the /auth/centers picker already hides such centers).
        await EnsureEnvironmentActiveAsync(environmentId, cancellationToken);

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
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        // M34 — the env is read off the stored token, not supplied by the caller.
        var existing = await _refreshStore.FindActiveByTokenAsync(rawRefreshToken, cancellationToken)
                       ?? throw new ForbiddenException("invalid_refresh_token", "Refresh token is invalid or expired.");

        var environmentId = existing.EnvironmentId;

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

        // A suspended center also kills refresh, so it can't outlive suspension by 30 days.
        if (!await IsEnvironmentActiveAsync(environmentId, cancellationToken))
        {
            await _refreshStore.RevokeAsync(existing, "environment_suspended", cancellationToken);
            throw new ForbiddenException("environment_suspended", "This center is suspended.");
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

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var existing = await _refreshStore.FindActiveByTokenAsync(rawRefreshToken, cancellationToken);
        if (existing is null)
        {
            return; // logout is best-effort; an unknown/expired token is a no-op.
        }

        await _refreshStore.RevokeAsync(existing, "logout", cancellationToken);
    }

    /// <summary>
    /// Lists the ACTIVE centers an ACTIVE user with this phone belongs to — the login-routing
    /// picker. Anonymous + IP-rate-limited; the phone-existence exposure is accepted (the chosen
    /// phone-lookup UX) and uniform (always 200, empty list when none).
    /// </summary>
    public async Task<IReadOnlyList<CenterOption>> FindCentersForPhoneAsync(
        string phone,
        CancellationToken cancellationToken)
    {
        var trimmed = phone.Trim();
        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.PhonePrimary == trimmed && u.Status == UserStatus.Active && u.DeletedAt == null)
            .Join(
                _db.Environments.IgnoreQueryFilters()
                    .Where(e => e.Status == EnvironmentStatus.Active && e.DeletedAt == null),
                u => u.EnvironmentId,
                e => e.Id,
                (_, e) => new CenterOption(e.Id, e.Name, e.Code))
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Resolves an ACTIVE center by its human code — the registration-routing picker.</summary>
    public async Task<CenterOption?> FindCenterByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var trimmed = code.Trim();
        return await _db.Environments
            .IgnoreQueryFilters()
            .Where(e => e.Code == trimmed && e.Status == EnvironmentStatus.Active && e.DeletedAt == null)
            .Select(e => new CenterOption(e.Id, e.Name, e.Code))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task EnsureEnvironmentActiveAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        if (!await IsEnvironmentActiveAsync(environmentId, cancellationToken))
        {
            throw new ForbiddenException(
                "environment_suspended",
                "This center is suspended. Contact the platform administrator.");
        }
    }

    private Task<bool> IsEnvironmentActiveAsync(Guid environmentId, CancellationToken cancellationToken)
        => _db.Environments
            .IgnoreQueryFilters()
            .AnyAsync(
                e => e.Id == environmentId && e.Status == EnvironmentStatus.Active && e.DeletedAt == null,
                cancellationToken);

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
