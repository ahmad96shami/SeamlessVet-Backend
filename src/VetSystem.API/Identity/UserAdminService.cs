using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Identity;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Identity;

/// <summary>
/// Admin operations on users and registration requests. On any approve/override mutation we
/// invalidate the affected user's permission cache via <see cref="IPermissionResolver.Invalidate"/>
/// so the gate sees the change on the next request.
/// </summary>
public sealed class UserAdminService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly INumberPrefixGenerator _prefixes;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IClock _clock;
    private readonly ICurrentUserAccessor _currentUser;

    public UserAdminService(
        ApplicationDbContext db,
        INumberPrefixGenerator prefixes,
        IPermissionResolver permissionResolver,
        IClock clock,
        ICurrentUserAccessor currentUser)
    {
        _db = db;
        _prefixes = prefixes;
        _permissionResolver = permissionResolver;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<RegistrationRequestSummary>> ListPendingAsync(
        string? statusFilter,
        CancellationToken cancellationToken)
    {
        var status = statusFilter ?? RequestStatus.Pending;
        if (!RequestStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_status", $"status '{status}' is not a valid request status.");
        }

        return await _db.RegistrationRequests
            .Where(r => r.Status == status)
            .Join(_db.Users, r => r.UserId, u => u.Id, (r, u) => new { r, u })
            .OrderByDescending(x => x.r.CreatedAt)
            .Select(x => new RegistrationRequestSummary(
                x.r.Id,
                x.u.Id,
                x.u.FullName,
                x.u.PhonePrimary,
                x.u.Email,
                x.r.RequestedRoleKey,
                x.r.Status,
                x.r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The live user roster (GET /admin/users). Env-scoped by the global query filter; offset-paged
    /// (admin tables, TECH_STACK API Design Notes). Optional case-insensitive search (name/phone/email)
    /// and role/status filters. Never returns the password hash.
    /// </summary>
    public async Task<IReadOnlyList<UserResponse>> ListUsersAsync(
        string? search,
        string? role,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (status is not null && !UserStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_status", $"status '{status}' is not a valid user status.");
        }

        if (role is not null && !RoleKey.All.Contains(role))
        {
            throw new ConflictException("invalid_role", $"role '{role}' is not a valid role key.");
        }

        var query = _db.Users
            .AsNoTracking()
            .Join(_db.Roles, u => u.RoleId, r => r.Id, (u, r) => new { u, r });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.u.FullName, pattern) ||
                EF.Functions.ILike(x.u.PhonePrimary, pattern) ||
                (x.u.Email != null && EF.Functions.ILike(x.u.Email, pattern)));
        }

        if (role is not null)
        {
            query = query.Where(x => x.r.Key == role);
        }

        if (status is not null)
        {
            query = query.Where(x => x.u.Status == status);
        }

        return await query
            .OrderBy(x => x.u.FullName)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .Select(x => new UserResponse(
                x.u.Id,
                x.u.FullName,
                x.u.PhonePrimary,
                x.u.Email,
                x.r.Key,
                x.r.Name,
                x.u.Status,
                x.u.NumberPrefix,
                x.u.LicenseNumber,
                x.u.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>A single user + their permission overrides (GET /admin/users/{id}).</summary>
    public async Task<UserDetailResponse> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Join(_db.Roles, u => u.RoleId, r => r.Id, (u, r) => new { u, r })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("user", id);

        var overrides = await _db.UserPermissionOverrides
            .AsNoTracking()
            .Where(o => o.UserId == id && o.DeletedAt == null)
            .Join(
                _db.Permissions,
                o => o.PermissionId,
                p => p.Id,
                (o, p) => new UserPermissionOverrideItem(p.Key, o.Effect))
            .ToListAsync(cancellationToken);

        return new UserDetailResponse(
            row.u.Id,
            row.u.FullName,
            row.u.PhonePrimary,
            row.u.Email,
            row.r.Key,
            row.r.Name,
            row.u.Status,
            row.u.NumberPrefix,
            row.u.LicenseNumber,
            row.u.LicenseDetails,
            row.u.CreatedAt,
            overrides);
    }

    public async Task<RegistrationRequestSummary> ApproveAsync(
        Guid registrationRequestId,
        ApproveRequest request,
        CancellationToken cancellationToken)
    {
        var reviewerId = RequireReviewerId();

        var (req, user) = await LoadPendingAsync(registrationRequestId, cancellationToken);

        user.Status = UserStatus.Active;
        user.NumberPrefix = await _prefixes.GenerateUniqueAsync(user.EnvironmentId, cancellationToken);

        req.Status = RequestStatus.Approved;
        req.ReviewedBy = reviewerId;
        req.ReviewedAt = _clock.UtcNow;
        req.ReviewNotes = request.Notes;

        // M4 task 3 — provision the field doctor's "moving warehouse" in the same transaction.
        await EnsureFieldInventoryAsync(user, req.RequestedRoleKey, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _permissionResolver.Invalidate(user.Id);

        return new RegistrationRequestSummary(
            req.Id, user.Id, user.FullName, user.PhonePrimary, user.Email,
            req.RequestedRoleKey, req.Status, req.CreatedAt);
    }

    public async Task<RegistrationRequestSummary> RejectAsync(
        Guid registrationRequestId,
        RejectRequest request,
        CancellationToken cancellationToken)
    {
        var reviewerId = RequireReviewerId();
        var (req, user) = await LoadPendingAsync(registrationRequestId, cancellationToken);

        req.Status = RequestStatus.Rejected;
        req.ReviewedBy = reviewerId;
        req.ReviewedAt = _clock.UtcNow;
        req.ReviewNotes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);

        return new RegistrationRequestSummary(
            req.Id, user.Id, user.FullName, user.PhonePrimary, user.Email,
            req.RequestedRoleKey, req.Status, req.CreatedAt);
    }

    public Task DeactivateAsync(Guid userId, CancellationToken cancellationToken)
        => SetStatusAsync(userId, UserStatus.Suspended, cancellationToken);

    public Task ReactivateAsync(Guid userId, CancellationToken cancellationToken)
        => SetStatusAsync(userId, UserStatus.Active, cancellationToken);

    public async Task AddPermissionOverrideAsync(
        Guid userId,
        PermissionOverrideRequest request,
        CancellationToken cancellationToken)
    {
        if (!OverrideEffect.All.Contains(request.Effect))
        {
            throw new ConflictException("invalid_effect", $"effect '{request.Effect}' is not 'grant' or 'deny'.");
        }

        var user = await LoadUserAsync(userId, cancellationToken);

        var permission = await _db.Permissions
            .FirstOrDefaultAsync(p => p.EnvironmentId == user.EnvironmentId && p.Key == request.PermissionKey, cancellationToken)
            ?? throw new NotFoundException("permission", request.PermissionKey);

        var existing = await _db.UserPermissionOverrides
            .FirstOrDefaultAsync(
                o => o.UserId == user.Id && o.PermissionId == permission.Id && o.DeletedAt == null,
                cancellationToken);

        if (existing is null)
        {
            _db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                EnvironmentId = user.EnvironmentId,
                UserId = user.Id,
                PermissionId = permission.Id,
                Effect = request.Effect,
            });
        }
        else
        {
            existing.Effect = request.Effect;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _permissionResolver.Invalidate(user.Id);
    }

    private async Task SetStatusAsync(Guid userId, string status, CancellationToken cancellationToken)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        user.Status = status;
        await _db.SaveChangesAsync(cancellationToken);
        _permissionResolver.Invalidate(user.Id);
    }

    private async Task<User> LoadUserAsync(Guid userId, CancellationToken cancellationToken)
        => await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
           ?? throw new NotFoundException("user", userId);

    private async Task<(RegistrationRequest req, User user)> LoadPendingAsync(
        Guid registrationRequestId,
        CancellationToken cancellationToken)
    {
        var req = await _db.RegistrationRequests.FirstOrDefaultAsync(
            r => r.Id == registrationRequestId,
            cancellationToken)
            ?? throw new NotFoundException("registration_request", registrationRequestId);

        if (req.Status != RequestStatus.Pending)
        {
            throw new ConflictException(
                "registration_already_reviewed",
                $"Registration request is already '{req.Status}'.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, cancellationToken)
                   ?? throw new NotFoundException("user", req.UserId);

        return (req, user);
    }

    /// <summary>
    /// M4 task 3 — a field doctor's field inventory (SCHEMA §4 "moving warehouse") is provisioned
    /// on approval so it exists before any load-to-field movement. Added to the change tracker and
    /// saved atomically with the approval. Idempotent: a pre-existing row (re-approval, manual seed)
    /// no-ops. Clinic-only roles get no field inventory.
    /// </summary>
    private async Task EnsureFieldInventoryAsync(User user, string roleKey, CancellationToken cancellationToken)
    {
        if (roleKey != RoleKey.VetField && roleKey != RoleKey.VetBoth)
        {
            return;
        }

        var exists = await _db.FieldInventories.AnyAsync(f => f.DoctorId == user.Id, cancellationToken);
        if (exists)
        {
            return;
        }

        _db.FieldInventories.Add(new FieldInventory
        {
            EnvironmentId = user.EnvironmentId,
            DoctorId = user.Id,
        });
    }

    private Guid RequireReviewerId() =>
        _currentUser.UserId
        ?? throw new ForbiddenException("unauthenticated", "Reviewer identity could not be resolved from the request.");
}
