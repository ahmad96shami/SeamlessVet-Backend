using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Customers;

/// <summary>
/// M3 task 15 / M14 — confirms PowerSync exposes a doctor's assigned customers (and their pets /
/// ledger / ledger_entries) without leaking other doctors' rows. PowerSync forbids JOINs, so M14
/// reworked this into Sync Streams: the <c>doctor_owned</c> stream selects the doctor's own
/// customers (assigned_doctor_id = auth.user_id()), and the <c>by_customer</c> stream — anchored by the
/// <c>my_customers</c> CTE — pulls their children by a single-table
/// <c>customer_id IN (SELECT customer_id FROM my_customers)</c> filter (ledger_entries reaching
/// customer_id via the M14 denormalized scope key). Two checks:
///
/// 1. <c>powersync/sync-rules.yaml</c> declares the <c>doctor_owned</c> + <c>by_customer</c> streams with
///    the expected parameter queries and single-table (JOIN-free) data filters.
/// 2. The same scope, evaluated against Postgres (what PowerSync's replication mirrors), returns only
///    the rows belonging to the doctor whose id matches the bucket parameter.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorScopeSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DeclaresDoctorScopeBucket()
    {
        var rulesPath = LocateSyncRulesFile();
        var contents = File.ReadAllText(rulesPath);

        contents.Should().Contain("auth.user_id()", "scoping must be parameterised by the JWT user (Sync Streams syntax)");
        contents.Should().Contain("doctor_owned:", "the doctor's own entities live in the `doctor_owned` stream");
        contents.Should().Contain("by_customer:", "a customer's children live in the `by_customer` stream");

        // The doctor_owned stream selects the doctor's own customers; by_customer is parameterized by them.
        contents.Should().MatchRegex(@"FROM\s+customers\s+WHERE\s+assigned_doctor_id\s*=\s*auth\.user_id\(\)",
            "the doctor_owned stream selects customers assigned to the doctor (PRD §8.6)");
        contents.Should().MatchRegex(@"my_customers:\s*SELECT\s+id\s+AS\s+customer_id\s+FROM\s+customers\s+WHERE\s+assigned_doctor_id\s*=\s*auth\.user_id\(\)",
            "the my_customers CTE anchors the by_customer stream to the doctor's assigned customers");

        // Children are reached by a single-table filter against the my_customers CTE — never a JOIN.
        contents.Should().MatchRegex(@"FROM\s+pets\s+WHERE\s+customer_id\s+IN\s+\(SELECT\s+customer_id\s+FROM\s+my_customers\)",
            "pets are scoped by the by_customer stream");
        contents.Should().MatchRegex(@"FROM\s+farms\s+WHERE\s+customer_id\s+IN\s+\(SELECT\s+customer_id\s+FROM\s+my_customers\)",
            "farms (M15) are scoped by the by_customer stream, inheriting the customer's doctor");
        contents.Should().MatchRegex(@"FROM\s+ledgers\s+WHERE\s+customer_id\s+IN\s+\(SELECT\s+customer_id\s+FROM\s+my_customers\)",
            "ledgers are scoped by the by_customer stream");
        contents.Should().MatchRegex(@"FROM\s+ledger_entries\s+WHERE\s+customer_id\s+IN\s+\(SELECT\s+customer_id\s+FROM\s+my_customers\)",
            "ledger_entries reach customer_id via the M14 denormalized scope key");

        // M17 — night stays (مبيت) are children of the doctor's own visits (by_visit stream).
        contents.Should().Contain("by_visit:", "a visit's clinical children live in the `by_visit` stream");
        contents.Should().MatchRegex(@"FROM\s+night_stays\s+WHERE\s+visit_id\s+IN\s+\(SELECT\s+visit_id\s+FROM\s+my_visits\)",
            "night_stays (M17) are scoped through the doctor's own visits");
    }

    [Fact]
    public async Task DoctorScope_Filter_ReturnsOnlyAssignedDoctorRows()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var doctorA = await SeedFieldDoctorAsync(scope, "+97A-" + Guid.NewGuid().ToString("N")[..6]);
        var doctorB = await SeedFieldDoctorAsync(scope, "+97B-" + Guid.NewGuid().ToString("N")[..6]);

        await using var writeDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = doctorA.Id,
        });

        var (customerA, ledgerA, _) = await SeedCustomerWithChildrenAsync(writeDb, scope.EnvironmentId, doctorA.Id);
        var (customerB, ledgerB, _) = await SeedCustomerWithChildrenAsync(writeDb, scope.EnvironmentId, doctorB.Id);

        // Simulate the doctor_scope WHERE clause directly. Bypass the env-scoped query filter to
        // emulate what PowerSync's replication stream observes: the bucket parameter is the only
        // thing that should scope rows in the assigned-doctor dimension.
        await using var verifyDb = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = doctorA.Id,
        });

        var visibleToDoctorA = await verifyDb.Customers
            .IgnoreQueryFilters()
            .Where(c => c.EnvironmentId == scope.EnvironmentId
                        && c.AssignedDoctorId == doctorA.Id
                        && c.DeletedAt == null)
            .Select(c => c.Id)
            .ToListAsync();

        visibleToDoctorA.Should().Contain(customerA, "doctor A's bucket must include A's customer");
        visibleToDoctorA.Should().NotContain(customerB, "doctor A's bucket must NOT include B's customer");

        // Pets FK-chain filter
        var petsForDoctorA = await verifyDb.Pets
            .IgnoreQueryFilters()
            .Join(verifyDb.Customers.IgnoreQueryFilters(),
                p => p.CustomerId, c => c.Id, (p, c) => new { p, c })
            .Where(x => x.c.EnvironmentId == scope.EnvironmentId
                        && x.c.AssignedDoctorId == doctorA.Id
                        && x.p.DeletedAt == null)
            .Select(x => x.p.Id)
            .ToListAsync();
        petsForDoctorA.Should().NotBeEmpty("doctor A's pet must flow through the FK chain");

        // Ledger FK-chain filter
        var ledgersForDoctorA = await verifyDb.Ledgers
            .IgnoreQueryFilters()
            .Join(verifyDb.Customers.IgnoreQueryFilters(),
                l => l.CustomerId, c => c.Id, (l, c) => new { l, c })
            .Where(x => x.c.EnvironmentId == scope.EnvironmentId
                        && x.c.AssignedDoctorId == doctorA.Id
                        && x.l.DeletedAt == null)
            .Select(x => x.l.Id)
            .ToListAsync();
        ledgersForDoctorA.Should().Contain(ledgerA);
        ledgersForDoctorA.Should().NotContain(ledgerB);

        // Ledger-entries FK-chain (ledger.customer.assigned_doctor_id)
        var entriesForDoctorA = await verifyDb.LedgerEntries
            .IgnoreQueryFilters()
            .Join(verifyDb.Ledgers.IgnoreQueryFilters(),
                e => e.LedgerId, l => l.Id, (e, l) => new { e, l })
            .Join(verifyDb.Customers.IgnoreQueryFilters(),
                joined => joined.l.CustomerId, c => c.Id, (joined, c) => new { joined.e, joined.l, c })
            .Where(x => x.c.EnvironmentId == scope.EnvironmentId
                        && x.c.AssignedDoctorId == doctorA.Id
                        && x.e.DeletedAt == null)
            .Select(x => x.e.LedgerId)
            .ToListAsync();
        entriesForDoctorA.Should().OnlyContain(lid => lid == ledgerA,
            "doctor A's ledger entries must not surface entries from B's ledger");
    }

    /// <summary>
    /// M14 — the denormalized scope keys the JOIN-free sync rules depend on are populated server-side by
    /// BEFORE INSERT/UPDATE triggers, derived from the immutable parent FK. A client-supplied value can
    /// never widen a row's scope.
    /// </summary>
    [Fact]
    public async Task Triggers_DeriveDenormalizedScopeKeysFromParent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var doctor = await SeedFieldDoctorAsync(scope, "+97T-" + Guid.NewGuid().ToString("N")[..6]);

        await using var db = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = doctor.Id,
        });
        var (customerId, _, entryId) = await SeedCustomerWithChildrenAsync(db, scope.EnvironmentId, doctor.Id);

        // ledger_entries.customer_id (a shadow column) is copied from the parent ledger by the trigger.
        var entryCustomerId = await db.LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.Id == entryId)
            .Select(e => EF.Property<Guid?>(e, "CustomerId"))
            .FirstAsync();
        entryCustomerId.Should().Be(customerId,
            "the BEFORE INSERT trigger copies the parent ledger's customer_id onto ledger_entries");

        // vaccinations.customer_id is forced to the pet's owner even when a bogus value is supplied.
        var petId = await db.Pets.IgnoreQueryFilters()
            .Where(p => p.CustomerId == customerId).Select(p => p.Id).FirstAsync();
        var vaccinationId = Guid.CreateVersion7();
        db.Vaccinations.Add(new Vaccination
        {
            Id = vaccinationId,
            EnvironmentId = scope.EnvironmentId,
            PetId = petId,
            CustomerId = Guid.CreateVersion7(), // deliberately wrong — the trigger must overwrite it
            VaccineType = "rabies",
            DateGiven = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var vaccinationCustomerId = await db.Vaccinations.IgnoreQueryFilters()
            .Where(v => v.Id == vaccinationId).Select(v => v.CustomerId).FirstAsync();
        vaccinationCustomerId.Should().Be(customerId,
            "the trigger derives vaccination.customer_id from the pet's owner, ignoring a client-supplied value");
    }

    private static async Task<User> SeedFieldDoctorAsync(PgTestScope scope, string phone)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var vetFieldRole = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.VetField);

        if (vetFieldRole is null)
        {
            vetFieldRole = new Role
            {
                Id = Guid.CreateVersion7(),
                EnvironmentId = scope.EnvironmentId,
                Key = RoleKey.VetField,
                Name = "Field Vet",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Roles.Add(vetFieldRole);
            await db.SaveChangesAsync();
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            RoleId = vetFieldRole.Id,
            FullName = "Field Doctor " + phone,
            PhonePrimary = phone,
            PasswordHash = "$2a$12$test.hash.placeholder.for.scope.test",
            Status = UserStatus.Active,
            NumberPrefix = $"D{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(Guid CustomerId, Guid LedgerId, Guid LedgerEntryId)> SeedCustomerWithChildrenAsync(
        VetSystem.Infrastructure.Persistence.ApplicationDbContext db,
        Guid environmentId,
        Guid assignedDoctorId)
    {
        var now = DateTimeOffset.UtcNow;
        var customerId = Guid.CreateVersion7();
        var ledgerId = Guid.CreateVersion7();
        var petId = Guid.CreateVersion7();
        var entryId = Guid.CreateVersion7();

        db.Customers.Add(new Customer
        {
            Id = customerId,
            EnvironmentId = environmentId,
            Type = CustomerType.PoultryFarm,
            FullName = "Farm-" + customerId.ToString("N")[..6],
            AssignedDoctorId = assignedDoctorId,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.Pets.Add(new Pet
        {
            Id = petId,
            EnvironmentId = environmentId,
            CustomerId = customerId,
            Name = "Flock",
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.Ledgers.Add(new Ledger
        {
            Id = ledgerId,
            EnvironmentId = environmentId,
            CustomerId = customerId,
            Balance = 100m,
            Status = LedgerStatus.HasDebt,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = entryId,
            EnvironmentId = environmentId,
            LedgerId = ledgerId,
            EntryType = LedgerEntryType.Adjustment,
            Amount = 100m,
            BalanceAfter = 100m,
            IdempotencyKey = $"seed-{entryId:N}",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return (customerId, ledgerId, entryId);
    }

    private static string LocateSyncRulesFile()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "powersync", "sync-rules.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("powersync/sync-rules.yaml not found above the test binary.");
    }
}
