using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Customers;

/// <summary>
/// M3 task 15 — confirms PowerSync's <c>doctor_scope</c> bucket exposes a doctor's assigned
/// customers (and their pets / ledger / ledger_entries by FK chain) without leaking
/// other doctors' rows. Two checks:
///
/// 1. <c>powersync/sync-rules.yaml</c> declares the bucket with the four expected SELECTs
///    parameterized by <c>users.id = request.user_id()</c>.
/// 2. Running the same WHERE-clause filter directly against Postgres (what PowerSync's
///    replication stream would mirror) returns only the rows that belong to the doctor whose
///    id matches the bucket parameter.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DoctorScopeSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DeclaresDoctorScopeBucket()
    {
        var rulesPath = LocateSyncRulesFile();
        var contents = File.ReadAllText(rulesPath);

        contents.Should().Contain("doctor_scope:", "M3 introduces the doctor_scope bucket");
        contents.Should().Contain("request.user_id()", "scoping must be parameterised by the JWT user");

        contents.Should().MatchRegex(@"SELECT\s+\*\s+FROM\s+customers",
            "doctor_scope must select customers assigned to the doctor");
        contents.Should().MatchRegex(@"FROM\s+pets",
            "doctor_scope must select pets via the customer FK chain");
        contents.Should().MatchRegex(@"FROM\s+ledgers",
            "doctor_scope must select ledgers via the customer FK chain");
        contents.Should().MatchRegex(@"FROM\s+ledger_entries",
            "doctor_scope must select ledger_entries via the ledger FK chain");

        contents.Should().Contain("assigned_doctor_id = bucket.doctor_id",
            "rows must be filtered by the assigned doctor (PRD §8.6)");
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
