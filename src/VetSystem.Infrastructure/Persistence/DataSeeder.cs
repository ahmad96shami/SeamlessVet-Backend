using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Infrastructure.Persistence;

/// <summary>
/// Idempotent bootstrap seeder. Source of truth for first-run data (per <c>vet-backend/CLAUDE.md</c>).
/// Order: migrate → bootstrap env → roles → permissions → role-permission defaults → bootstrap admin.
/// </summary>
public sealed class DataSeeder
{
    public static readonly Guid BootstrapEnvironmentId = Guid.Parse("01900000-0000-7000-8000-000000000001");

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IGuidV7Generator _ids;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        ApplicationDbContext db,
        IClock clock,
        IGuidV7Generator ids,
        IPasswordHasher hasher,
        IConfiguration configuration,
        ILogger<DataSeeder> logger)
    {
        _db = db;
        _clock = clock;
        _ids = ids;
        _hasher = hasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _db.Database.MigrateAsync(cancellationToken);

        if (force)
        {
            _logger.LogWarning("DataSeeder running with --force-seed: existing seed data will be cleared.");
            await ClearAsync(cancellationToken);
        }

        await SeedBootstrapEnvironmentAsync(cancellationToken);

        var environments = await _db.Environments
            .IgnoreQueryFilters()
            .Where(e => e.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var env in environments)
        {
            await SeedRolesAsync(env.Id, cancellationToken);
            await SeedPermissionsAsync(env.Id, cancellationToken);
            await SeedRolePermissionDefaultsAsync(env.Id, cancellationToken);
        }

        await SeedBootstrapAdminAsync(BootstrapEnvironmentId, cancellationToken);
    }

    private async Task SeedBootstrapEnvironmentAsync(CancellationToken cancellationToken)
    {
        var exists = await _db.Environments
            .IgnoreQueryFilters()
            .AnyAsync(e => e.Id == BootstrapEnvironmentId, cancellationToken);

        if (exists)
        {
            return;
        }

        var now = _clock.UtcNow;
        _db.Environments.Add(new DomainEnvironment
        {
            Id = BootstrapEnvironmentId,
            Name = "Bootstrap",
            Mode = EnvironmentMode.Solo,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded bootstrap environment {EnvironmentId}", BootstrapEnvironmentId);
    }

    private async Task SeedRolesAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var existing = await _db.Roles
            .IgnoreQueryFilters()
            .Where(r => r.EnvironmentId == environmentId)
            .Select(r => r.Key)
            .ToListAsync(cancellationToken);

        var missing = RoleKey.All.Except(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var key in missing)
        {
            _db.Roles.Add(new Role
            {
                EnvironmentId = environmentId,
                Key = key,
                Name = DisplayNameFor(key),
            });
        }

        if (missing.Any())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedPermissionsAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var existing = await _db.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.EnvironmentId == environmentId)
            .Select(p => p.Key)
            .ToListAsync(cancellationToken);

        var missing = PermissionKey.All.Except(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var key in missing)
        {
            _db.Permissions.Add(new Permission
            {
                EnvironmentId = environmentId,
                Key = key,
                Description = DescribePermission(key),
            });
        }

        if (missing.Any())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedRolePermissionDefaultsAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var defaults = BuildRoleDefaults();

        var roles = await _db.Roles
            .IgnoreQueryFilters()
            .Where(r => r.EnvironmentId == environmentId)
            .ToDictionaryAsync(r => r.Key, r => r.Id, cancellationToken);

        var permissions = await _db.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.EnvironmentId == environmentId)
            .ToDictionaryAsync(p => p.Key, p => p.Id, cancellationToken);

        var existingPairs = await _db.RolePermissions
            .Where(rp => roles.Values.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToHashSetAsync(cancellationToken);

        var added = 0;
        foreach (var (roleKey, permKeys) in defaults)
        {
            if (!roles.TryGetValue(roleKey, out var roleId))
            {
                continue;
            }

            foreach (var permKey in permKeys)
            {
                if (!permissions.TryGetValue(permKey, out var permId))
                {
                    continue;
                }

                var pair = new { RoleId = roleId, PermissionId = permId };
                if (existingPairs.Contains(pair))
                {
                    continue;
                }

                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permId,
                });
                added++;
            }
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedBootstrapAdminAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var section = _configuration.GetSection("BootstrapAdmin");
        var phone = section["PhonePrimary"];
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password)
            || password.StartsWith("PLACEHOLDER", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "BootstrapAdmin:Password not configured (placeholder still in appsettings). "
                + "Skipping admin user seed. Set via dotnet user-secrets to enable.");
            return;
        }

        var exists = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EnvironmentId == environmentId && u.PhonePrimary == phone, cancellationToken);

        if (exists)
        {
            return;
        }

        var adminRole = await _db.Roles
            .IgnoreQueryFilters()
            .FirstAsync(
                r => r.EnvironmentId == environmentId && r.Key == RoleKey.Admin,
                cancellationToken);

        var now = _clock.UtcNow;
        _db.Users.Add(new User
        {
            EnvironmentId = environmentId,
            RoleId = adminRole.Id,
            FullName = section["FullName"] ?? "Bootstrap Admin",
            PhonePrimary = phone,
            Email = section["Email"],
            PasswordHash = _hasher.Hash(password),
            Status = UserStatus.Active,
            NumberPrefix = "ADM",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded bootstrap admin user in environment {EnvironmentId}", environmentId);
    }

    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
              refresh_tokens,
              registration_requests,
              user_permission_overrides,
              role_permissions,
              users,
              roles,
              permissions,
              idempotency_keys,
              sync_test_records,
              environments
            RESTART IDENTITY CASCADE;
            """,
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> BuildRoleDefaults() =>
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [RoleKey.Admin] = PermissionKey.All, // admin gets every catalog permission
            [RoleKey.Accountant] =
            [
                PermissionKey.InvoicesRefund,
                PermissionKey.InvoicesVoid,
                PermissionKey.ReportsRead,
                PermissionKey.SettingsWrite,
            ],
            [RoleKey.InventoryStaff] = [PermissionKey.InventoryAdjust],
            // M5/M7/M8 will append clinic/field/POS/contract permissions as their milestones land.
            [RoleKey.VetClinic] = Array.Empty<string>(),
            [RoleKey.VetField] = Array.Empty<string>(),
            [RoleKey.VetBoth] = Array.Empty<string>(),
            [RoleKey.Receptionist] = Array.Empty<string>(),
            [RoleKey.Cashier] = Array.Empty<string>(),
        };

    private static string DisplayNameFor(string key) => key switch
    {
        RoleKey.Admin => "Administrator",
        RoleKey.Accountant => "Accountant",
        RoleKey.VetClinic => "Clinic Veterinarian",
        RoleKey.VetField => "Field Veterinarian",
        RoleKey.VetBoth => "Clinic + Field Veterinarian",
        RoleKey.Receptionist => "Receptionist",
        RoleKey.Cashier => "Cashier",
        RoleKey.InventoryStaff => "Inventory Staff",
        _ => key,
    };

    private static string DescribePermission(string key) => key switch
    {
        PermissionKey.UsersApprove => "Approve or reject registration requests.",
        PermissionKey.UsersManage => "Deactivate or reactivate user accounts.",
        PermissionKey.UsersPermissionsOverride => "Grant or deny individual permissions per user.",
        PermissionKey.SettingsWrite => "Edit environment-level system settings.",
        PermissionKey.ContractsActivate => "Promote draft contracts to active.",
        PermissionKey.InvoicesRefund => "Refund issued invoices.",
        PermissionKey.InvoicesVoid => "Void issued invoices.",
        PermissionKey.InventoryAdjust => "Apply inventory adjustments.",
        PermissionKey.ReportsRead => "View operational and financial reports.",
        _ => key,
    };
}
