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

    public async Task SeedAsync(bool force = false, bool demo = false, CancellationToken cancellationToken = default)
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

            if (demo)
            {
                await SeedDemoCatalogAsync(env.Id, cancellationToken);
            }
        }

        await SeedBootstrapAdminAsync(BootstrapEnvironmentId, cancellationToken);

        if (demo)
        {
            // Opening stock runs after the admin seed: every inventory_movement needs a valid
            // performed_by (FK to users), and the bootstrap admin is created above.
            foreach (var env in environments)
            {
                await SeedDemoStockAsync(env.Id, cancellationToken);
            }
        }
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

    /// <summary>
    /// Demo / showcase catalog — products (medications + general goods), billable services
    /// (clinic + field), and the vaccine catalog (services with category <c>vaccination</c>).
    /// Gated behind <c>--demo</c> so production first-run stays empty; idempotent (matches on
    /// Arabic name) so re-running <c>--seed --demo</c> never duplicates rows.
    /// </summary>
    private async Task SeedDemoCatalogAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        await SeedDemoProductsAsync(environmentId, cancellationToken);
        await SeedDemoServicesAsync(environmentId, cancellationToken);
    }

    private async Task SeedDemoProductsAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var existing = await _db.Products
            .IgnoreQueryFilters()
            .Where(p => p.EnvironmentId == environmentId)
            .Select(p => p.NameAr)
            .ToHashSetAsync(cancellationToken);

        // (NameAr, NameLatin, Category, Manufacturer, UnitOfMeasure, Purchase, Selling, Reorder)
        var seeds = new (string NameAr, string NameLatin, string Category, string? Manufacturer, string Unit, decimal Purchase, decimal Selling, decimal Reorder)[]
        {
            // — Medications —
            ("أموكسيسيلين 15% حقن", "Amoxicillin 15% LA", ProductCategory.Medication, "Interchemie", "قارورة 100 مل", 18m, 30m, 10m),
            ("أوكسي تتراسيكلين 20% طويل المفعول", "Oxytetracycline 20% LA", ProductCategory.Medication, "Vetoquinol", "قارورة 100 مل", 22m, 38m, 10m),
            ("إنروفلوكساسين 10% حقن", "Enrofloxacin 10%", ProductCategory.Medication, "Bayer", "قارورة 100 مل", 25m, 42m, 8m),
            ("بنسلين - ستربتومايسين", "Penicillin–Streptomycin", ProductCategory.Medication, "Norbrook", "قارورة 100 مل", 20m, 34m, 10m),
            ("ميلوكسيكام 2% حقن", "Meloxicam 2%", ProductCategory.Medication, "Boehringer Ingelheim", "قارورة 50 مل", 30m, 50m, 6m),
            ("ديكساميثازون حقن", "Dexamethasone", ProductCategory.Medication, "MSD", "قارورة 50 مل", 12m, 22m, 8m),
            ("آيفرمكتين 1% حقن", "Ivermectin 1%", ProductCategory.Medication, "Merial", "قارورة 50 مل", 15m, 28m, 10m),
            ("ألبيندازول 10% شراب", "Albendazole 10% Oral", ProductCategory.Medication, "Vapco", "عبوة 1 لتر", 16m, 28m, 6m),
            ("فيتامين AD3E حقن", "Vitamin AD3E", ProductCategory.Medication, "Interchemie", "قارورة 100 مل", 14m, 25m, 8m),
            ("محلول وريدي (نورمال سلاين)", "Normal Saline IV", ProductCategory.Medication, "Hikma", "كيس 1 لتر", 5m, 10m, 20m),
            ("أوكسيتوسين حقن", "Oxytocin", ProductCategory.Medication, "Vapco", "قارورة 50 مل", 8m, 16m, 6m),
            ("مرهم العين (أوكسي تتراسيكلين)", "Eye Ointment", ProductCategory.Medication, "Jamjoom Pharma", "أنبوب 5 غم", 4m, 9m, 12m),
            // — General products —
            ("علف قطط جاف", "Dry Cat Food", ProductCategory.Product, "Royal Canin", "كيس 2 كغ", 35m, 55m, 5m),
            ("علف كلاب جاف", "Dry Dog Food", ProductCategory.Product, "Pedigree", "كيس 3 كغ", 40m, 62m, 5m),
            ("شامبو طبي للحيوانات", "Medicated Pet Shampoo", ProductCategory.Product, "Beaphar", "عبوة 250 مل", 12m, 22m, 8m),
            ("طوق مضاد للبراغيث والقراد", "Flea & Tick Collar", ProductCategory.Product, "Bayer", "قطعة", 18m, 32m, 10m),
            ("قفص نقل بلاستيكي", "Plastic Pet Carrier", ProductCategory.Product, null, "قطعة", 45m, 75m, 3m),
            ("رباط شاش طبي", "Medical Gauze Roll", ProductCategory.Product, null, "قطعة", 2m, 5m, 30m),
            ("حقنة (سرنجة) 5 مل", "Syringe 5 ml", ProductCategory.Product, null, "قطعة", 0.3m, 1m, 100m),
            ("قفازات فحص (علبة)", "Exam Gloves Box", ProductCategory.Product, null, "علبة 100", 8m, 15m, 10m),
            // — Vaccines (M26 — stock products of category vaccine; the web اللقاحات tab + POS chip
            //   filter on this category, and administering one FEFO-deducts a dose) —
            ("لقاح السعار (داء الكلب)", "Rabies Vaccine", ProductCategory.Vaccine, "MSD", "جرعة", 22m, 40m, 10m),
            ("اللقاح الثماني للكلاب (DHPPi+L)", "Dog 8-in-1 (DHPPi+L)", ProductCategory.Vaccine, "Nobivac", "جرعة", 35m, 60m, 8m),
            ("اللقاح الرباعي للقطط (FVRCP)", "Feline FVRCP", ProductCategory.Vaccine, "Boehringer Ingelheim", "جرعة", 32m, 55m, 8m),
            ("لقاح السعار للقطط", "Feline Rabies", ProductCategory.Vaccine, "MSD", "جرعة", 22m, 40m, 8m),
            ("لقاح الحمى القلاعية", "Foot-and-Mouth Disease", ProductCategory.Vaccine, "MEVAC", "قارورة 50 جرعة", 14m, 25m, 6m),
            ("لقاح البروسيلا (Rev-1)", "Brucellosis Rev-1", ProductCategory.Vaccine, "Jovac", "قارورة 25 جرعة", 11m, 20m, 6m),
            ("لقاح جدري الأغنام", "Sheep Pox", ProductCategory.Vaccine, "MEVAC", "قارورة 50 جرعة", 10m, 18m, 6m),
            ("لقاح الكلوستريديا (المعوية)", "Clostridial / Enterotoxaemia", ProductCategory.Vaccine, "MSD", "قارورة 100 مل", 12m, 22m, 6m),
            ("لقاح طاعون المجترات الصغيرة", "Peste des Petits Ruminants", ProductCategory.Vaccine, "Jovac", "قارورة 100 جرعة", 11m, 20m, 6m),
            ("لقاح اللسان الأزرق", "Bluetongue", ProductCategory.Vaccine, "MEVAC", "قارورة 50 جرعة", 13m, 24m, 6m),
        };

        var added = 0;
        foreach (var s in seeds)
        {
            if (existing.Contains(s.NameAr))
            {
                continue;
            }

            _db.Products.Add(new Product
            {
                EnvironmentId = environmentId,
                NameAr = s.NameAr,
                NameLatin = s.NameLatin,
                Category = s.Category,
                Manufacturer = s.Manufacturer,
                UnitOfMeasure = s.Unit,
                PurchasePrice = s.Purchase,
                SellingPrice = s.Selling,
                ReorderPoint = s.Reorder,
            });
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Seeded {Count} demo products for environment {EnvironmentId}", added, environmentId);
        }
    }

    private async Task SeedDemoServicesAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var existing = await _db.Services
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => s.NameAr)
            .ToHashSetAsync(cancellationToken);

        // (NameAr, NameLatin, Category, DefaultPrice). Category is a free-text display label.
        // M26 — vaccines are no longer services; they live in the product catalog (category vaccine).
        var seeds = new (string NameAr, string NameLatin, string Category, decimal Price)[]
        {
            // — Clinic services —
            ("تعقيم / خصي قطة", "Cat Spay / Neuter", "جراحة", 120m),
            ("تعقيم / خصي كلب", "Dog Spay / Neuter", "جراحة", 180m),
            ("خياطة جرح", "Wound Suturing", "جراحة", 60m),
            ("تنظيف أسنان (تقليح)", "Dental Scaling", "عيادة", 80m),
            ("قص أظافر", "Nail Trimming", "عيادة", 15m),
            ("تصوير بالأشعة السينية", "X-Ray Imaging", "تشخيص", 70m),
            ("فحص بالموجات فوق الصوتية", "Ultrasound", "تشخيص", 90m),
            ("تحليل دم شامل (CBC)", "Complete Blood Count", "مختبر", 50m),
            ("تحليل براز", "Fecal Examination", "مختبر", 25m),
            // — Field services —
            ("زيارة مزرعة ميدانية", "Farm Field Visit", "خدمة ميدانية", 100m),
            ("فحص صحة القطيع", "Herd Health Check", "خدمة ميدانية", 150m),
            ("توليد متعسر", "Assisted Delivery", "خدمة ميدانية", 200m),
        };

        var added = 0;
        foreach (var s in seeds)
        {
            if (existing.Contains(s.NameAr))
            {
                continue;
            }

            _db.Services.Add(new Service
            {
                EnvironmentId = environmentId,
                NameAr = s.NameAr,
                NameLatin = s.NameLatin,
                Category = s.Category,
                DefaultPrice = s.Price,
            });
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Seeded {Count} demo services (incl. vaccines) for environment {EnvironmentId}", added, environmentId);
        }
    }

    /// <summary>
    /// Opening warehouse stock for the demo catalog so products are sellable in the POS. Writes the
    /// canonical pair per product — a <c>receive</c> <see cref="InventoryMovement"/> plus the
    /// materialized <see cref="StockItem"/> balance — keeping the invariant
    /// <c>stock_items.quantity == Σ quantity_delta</c> (trivially true for a single receive).
    /// Idempotent: skips any product already stocked at the central warehouse.
    /// </summary>
    private async Task SeedDemoStockAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var warehouseId = await _db.Warehouses
            .IgnoreQueryFilters()
            .Where(w => w.EnvironmentId == environmentId && w.DeletedAt == null)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouseId is null)
        {
            return; // no warehouse to receive into (should not happen — seeded above)
        }

        // performed_by is a required FK to users; attribute the opening stock to the admin.
        var performedBy = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.EnvironmentId == environmentId && u.DeletedAt == null)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (performedBy is null)
        {
            return; // no user to attribute the movement to
        }

        var products = await _db.Products
            .IgnoreQueryFilters()
            .Where(p => p.EnvironmentId == environmentId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.ReorderPoint, p.PurchasePrice, p.ExpirationDate })
            .ToListAsync(cancellationToken);

        var alreadyStocked = await _db.StockItems
            .IgnoreQueryFilters()
            .Where(s => s.EnvironmentId == environmentId
                && s.LocationType == StockLocation.Warehouse
                && s.LocationId == warehouseId.Value)
            .Select(s => s.ProductId)
            .ToHashSetAsync(cancellationToken);

        var added = 0;
        foreach (var p in products)
        {
            if (alreadyStocked.Contains(p.Id))
            {
                continue;
            }

            // Comfortably above the reorder point so nothing reads as low-stock in the demo.
            var quantity = Math.Max(25m, decimal.Round(p.ReorderPoint * 5m, 3));

            // M25 — opening stock is one FEFO lot at the catalog purchase cost (canonical trio:
            // lot + receive movement + materialized balance), keeping Σ remaining_qty == quantity.
            var lotId = Guid.CreateVersion7();
            _db.InventoryLots.Add(new InventoryLot
            {
                Id = lotId,
                EnvironmentId = environmentId,
                ProductId = p.Id,
                LocationType = StockLocation.Warehouse,
                LocationId = warehouseId.Value,
                UnitCost = p.PurchasePrice,
                ExpirationDate = p.ExpirationDate,
                ReceivedQty = quantity,
                RemainingQty = quantity,
                ReceivedAt = _clock.UtcNow,
            });

            _db.InventoryMovements.Add(new InventoryMovement
            {
                EnvironmentId = environmentId,
                ProductId = p.Id,
                MovementType = MovementType.Receive,
                ToLocationType = StockLocation.Warehouse,
                ToLocationId = warehouseId.Value,
                QuantityDelta = quantity,
                Reason = "رصيد افتتاحي (بيانات تجريبية)",
                LotId = lotId,
                PerformedBy = performedBy.Value,
                IdempotencyKey = $"seed-stock-{p.Id:N}",
            });

            _db.StockItems.Add(new StockItem
            {
                EnvironmentId = environmentId,
                LocationType = StockLocation.Warehouse,
                LocationId = warehouseId.Value,
                ProductId = p.Id,
                Quantity = quantity,
            });
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Seeded opening warehouse stock for {Count} demo products in environment {EnvironmentId}",
                added, environmentId);
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
              doctor_partner_ledger_entries,
              doctor_partner_payments,
              doctor_partner_ledgers,
              doctor_partners,
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
