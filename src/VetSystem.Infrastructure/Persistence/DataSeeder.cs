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
            await SeedSystemSettingsAsync(env.Id, cancellationToken);
            await SeedWarehouseAsync(env.Id, cancellationToken);
            await SeedSystemServicesAsync(env.Id, cancellationToken);
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

    private async Task SeedSystemSettingsAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var exists = await _db.SystemSettings
            .IgnoreQueryFilters()
            .AnyAsync(s => s.EnvironmentId == environmentId, cancellationToken);

        if (exists)
        {
            return;
        }

        _db.SystemSettings.Add(new SystemSettings
        {
            EnvironmentId = environmentId,
            DefaultExamFee = 0m,
            EntitlementEnabledGlobal = true,
            LowStockThresholdPct = 0m,
            ExpirationWarningDays = 30,
            TaxEnabled = false,
            TaxRate = 0m,
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded default system_settings row for environment {EnvironmentId}", environmentId);
    }

    private async Task SeedSystemServicesAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        // M23 — the checkup-fee / night-stay system services back the invoice lines for visit-care
        // charges (invoice_items is product-XOR-service). Issuance also find-or-creates them on
        // demand (API SystemServices), so this is convenience for fresh environments, not a gate.
        var seeds = new (string Category, string NameAr)[]
        {
            (ServiceCategories.Checkup, "رسوم الكشف"),
            (ServiceCategories.NightStay, "مبيت"),
        };

        foreach (var (category, nameAr) in seeds)
        {
            var exists = await _db.Services
                .IgnoreQueryFilters()
                .AnyAsync(s => s.EnvironmentId == environmentId && s.Category == category && s.DeletedAt == null,
                    cancellationToken);
            if (exists)
            {
                continue;
            }

            _db.Services.Add(new Service
            {
                EnvironmentId = environmentId,
                NameAr = nameAr,
                Category = category,
                DefaultPrice = 0m,
            });

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Seeded system service {Category} for environment {EnvironmentId}", category, environmentId);
        }
    }

    private async Task SeedWarehouseAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        // M4 task 2 — one central warehouse per environment. Field inventories are created on
        // demand when a field doctor is approved (UserAdminService), not here.
        var exists = await _db.Warehouses
            .IgnoreQueryFilters()
            .AnyAsync(w => w.EnvironmentId == environmentId, cancellationToken);

        if (exists)
        {
            return;
        }

        _db.Warehouses.Add(new Warehouse
        {
            EnvironmentId = environmentId,
            Name = "Central",
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded central warehouse for environment {EnvironmentId}", environmentId);
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
              doctor_entitlements,
              supplier_ledger_entries,
              supplier_payments,
              purchase_invoice_items,
              purchase_invoices,
              supplier_ledgers,
              suppliers,
              payments,
              invoice_items,
              receipt_vouchers,
              invoices,
              batches,
              contract_medication_prices,
              contracts,
              attachments,
              vaccinations,
              daily_follow_ups,
              prescriptions,
              procedures,
              visits,
              inventory_movements,
              stock_items,
              field_inventories,
              warehouses,
              ledger_entries,
              ledgers,
              pets,
              customers,
              refresh_tokens,
              registration_requests,
              user_permission_overrides,
              role_permissions,
              users,
              roles,
              permissions,
              idempotency_keys,
              sync_test_records,
              system_settings,
              products,
              services,
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
                PermissionKey.InvoicesWrite,
                PermissionKey.InvoicesRefund,
                PermissionKey.InvoicesVoid,
                PermissionKey.ReportsRead,
                PermissionKey.SettingsWrite,
                PermissionKey.CatalogWrite,
                PermissionKey.CustomersWrite,
                PermissionKey.ContractsWrite,
                PermissionKey.ContractsActivate,
                PermissionKey.EntitlementsApprove,
                // M19: the accountant manages suppliers, records purchase invoices, and pays suppliers.
                PermissionKey.SuppliersWrite,
            ],
            [RoleKey.InventoryStaff] = [PermissionKey.InventoryAdjust, PermissionKey.CatalogWrite],
            // M3: vet roles get customers.write so field doctors can author customers offline via
            // /sync/customers; the sync handler additionally restricts assigned_doctor_id writes.
            // M5: clinical roles get medical.write for the dedicated visit/medical endpoints; the
            // receptionist gets it too since they open visits at reception (PRD §3).
            // M6: clinical roles + the receptionist also get appointments.write so the front desk
            // and doctors can book/reschedule from either client.
            // M7: field-capable vets get invoices.write so they can issue field/exam-fee invoices and
            // receipt vouchers on-site (PRD §6.2). Clinic POS issuance is the cashier's job.
            // M8: field-capable vets get contracts.write so they can author/edit DRAFT contracts for
            // their assigned farms offline (PRD §6.6). They do NOT get contracts.activate by default —
            // promoting Draft → Active is an Admin/Accountant, online-confirmed action (PRD §8.9); grant
            // it to a field role only if the business wants full field autonomy.
            [RoleKey.VetClinic] = [PermissionKey.CustomersWrite, PermissionKey.MedicalWrite, PermissionKey.AppointmentsWrite],
            [RoleKey.VetField] = [PermissionKey.CustomersWrite, PermissionKey.MedicalWrite, PermissionKey.AppointmentsWrite, PermissionKey.InvoicesWrite, PermissionKey.ContractsWrite],
            [RoleKey.VetBoth] = [PermissionKey.CustomersWrite, PermissionKey.MedicalWrite, PermissionKey.AppointmentsWrite, PermissionKey.InvoicesWrite, PermissionKey.ContractsWrite],
            [RoleKey.Receptionist] = [PermissionKey.CustomersWrite, PermissionKey.MedicalWrite, PermissionKey.AppointmentsWrite],
            [RoleKey.Cashier] = [PermissionKey.InvoicesWrite],
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
        PermissionKey.CatalogWrite => "Create, edit, and remove products and services in the catalog.",
        PermissionKey.CustomersWrite => "Create, edit, and remove customers, pets, and ledger entries.",
        PermissionKey.MedicalWrite => "Create and edit visits, procedures, prescriptions, follow-ups, vaccinations, and attachments.",
        PermissionKey.AppointmentsWrite => "Create, reschedule, and resolve (attend/cancel/no-show) appointments.",
        PermissionKey.ContractsWrite => "Create and edit draft contracts, batches, and per-medication contract prices.",
        PermissionKey.ContractsActivate => "Promote draft contracts to active.",
        PermissionKey.InvoicesWrite => "Issue POS, field, and exam-fee invoices and receipt vouchers.",
        PermissionKey.InvoicesRefund => "Refund issued invoices.",
        PermissionKey.InvoicesVoid => "Void issued invoices.",
        PermissionKey.InventoryAdjust => "Apply inventory adjustments.",
        PermissionKey.EntitlementsApprove => "Close customer accounts and approve/pay doctor entitlements.",
        PermissionKey.PartnershipManage => "Manage partners and partnership profit shares (partnership environments only).",
        PermissionKey.ReportsRead => "View operational and financial reports.",
        PermissionKey.SuppliersWrite => "Manage suppliers, record purchase invoices, and pay suppliers.",
        _ => key,
    };
}
