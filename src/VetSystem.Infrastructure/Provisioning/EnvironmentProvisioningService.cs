using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VetSystem.Application.Common;
using VetSystem.Application.Provisioning;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Infrastructure.Provisioning;

/// <summary>
/// Stands up a tenant environment + its per-env identity/settings rows + a first active admin in one
/// transaction. The per-env seed steps are the idempotent ones extracted from <c>DataSeeder</c> so
/// the bootstrap path and the platform console share one source of truth. Entities carry an explicit
/// <c>EnvironmentId</c> so the auditing interceptor never needs a current user during provisioning.
/// </summary>
public sealed class EnvironmentProvisioningService : IEnvironmentProvisioningService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IGuidV7Generator _ids;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<EnvironmentProvisioningService> _logger;

    public EnvironmentProvisioningService(
        ApplicationDbContext db,
        IClock clock,
        IGuidV7Generator ids,
        IPasswordHasher hasher,
        ILogger<EnvironmentProvisioningService> logger)
    {
        _db = db;
        _clock = clock;
        _ids = ids;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<ProvisionEnvironmentResult> ProvisionAsync(
        ProvisionEnvironmentRequest request,
        Guid? environmentId,
        CancellationToken cancellationToken)
    {
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? EnvironmentMode.Solo : request.Mode.Trim();
        if (!EnvironmentMode.All.Contains(mode))
        {
            throw new ConflictException("invalid_environment_mode", $"Unknown environment mode '{mode}'.");
        }

        var code = request.Code.Trim();
        if (await _db.Environments.IgnoreQueryFilters().AnyAsync(e => e.Code == code, cancellationToken))
        {
            throw new ConflictException("environment_code_taken", $"Center code '{code}' is already in use.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;
        var envId = environmentId ?? _ids.New();
        _db.Environments.Add(new DomainEnvironment
        {
            Id = envId,
            Name = request.CenterName.Trim(),
            Code = code,
            Mode = mode,
            Status = EnvironmentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        await SeedStructureAsync(envId, cancellationToken);
        var admin = await CreateFirstAdminAsync(envId, request, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        _logger.LogInformation("Provisioned environment {EnvironmentId} ({Code})", envId, code);
        return new ProvisionEnvironmentResult(envId, admin.Id, code);
    }

    public async Task SeedStructureAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        await SeedRolesAsync(environmentId, cancellationToken);
        await SeedPermissionsAsync(environmentId, cancellationToken);
        await SeedRolePermissionDefaultsAsync(environmentId, cancellationToken);
        await SeedSystemSettingsAsync(environmentId, cancellationToken);
        await SeedWarehouseAsync(environmentId, cancellationToken);
        await SeedSystemServicesAsync(environmentId, cancellationToken);
    }

    private async Task<User> CreateFirstAdminAsync(
        Guid environmentId,
        ProvisionEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        var adminRole = await _db.Roles
            .IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == environmentId && r.Key == RoleKey.Admin, cancellationToken);

        var now = _clock.UtcNow;
        var admin = new User
        {
            EnvironmentId = environmentId,
            RoleId = adminRole.Id,
            FullName = request.AdminFullName.Trim(),
            PhonePrimary = request.AdminPhone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.AdminEmail) ? null : request.AdminEmail.Trim(),
            PasswordHash = _hasher.Hash(request.AdminPassword),
            Status = UserStatus.Active,
            NumberPrefix = "ADM",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(cancellationToken);
        return admin;
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
                // M19: the accountant manages suppliers, records purchase invoices, and pays suppliers.
                PermissionKey.SuppliersWrite,
                // M30: the accountant manages doctor-partners and pays their entitlement balances.
                PermissionKey.DoctorPartnersManage,
                PermissionKey.DoctorPartnersPay,
                // M31: the accountant manages employees (salaries/loans) and pays them.
                PermissionKey.EmployeesManage,
                PermissionKey.EmployeesPay,
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
        PermissionKey.PartnershipManage => "Manage partners and partnership profit shares (partnership environments only).",
        PermissionKey.ReportsRead => "View operational and financial reports.",
        PermissionKey.SuppliersWrite => "Manage suppliers, record purchase invoices, and pay suppliers.",
        PermissionKey.DoctorPartnersManage => "Manage doctor-partners (entitlement payees) and view their balances.",
        PermissionKey.DoctorPartnersPay => "Pay doctor-partners against their entitlement balances.",
        PermissionKey.EmployeesManage => "Manage employees (salaries, loans) and view their HR ledger balances.",
        PermissionKey.EmployeesPay => "Pay employees (salaries, loans, repayments).",
        _ => key,
    };
}
