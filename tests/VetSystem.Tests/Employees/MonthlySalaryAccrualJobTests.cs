using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.API.Jobs;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Employees;

/// <summary>
/// M31 task 4 + exit criterion — with a forced clock, <see cref="MonthlySalaryAccrualJob"/> posts one
/// <c>salary_accrual</c> per active employee per calendar month (the period idempotency key
/// <c>salary-accrual-{employeeId}-{yyyyMM}</c>), a same-month re-run is a no-op, advancing the clock to the
/// next month accrues again, and an inactive employee never accrues. Assertions are scoped to specific
/// period keys so the env-wide scan in other concurrently-running tests can't perturb them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MonthlySalaryAccrualJobTests
{
    [Fact]
    public async Task Accrues_ExactlyOncePerPeriod_AndIsIdempotent()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var employeeId = await SeedEmployeeAsync(scope, monthlySalary: 1200m, active: true);

        var clock = new FakeClock(new DateTimeOffset(2099, 3, 15, 9, 0, 0, TimeSpan.Zero));
        await using var factory = new VetApiFactory { Clock = clock };

        await RunJobAsync(factory);
        await RunJobAsync(factory); // same month → must not duplicate (period idempotency key)

        var key = $"salary-accrual-{employeeId}-209903";
        await using var db = NewContext(scope);
        var entries = await db.EmployeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.EnvironmentId == scope.EnvironmentId && e.IdempotencyKey == key)
            .ToListAsync();
        entries.Should().HaveCount(1, "the salary accrues exactly once per period, even across re-runs");
        entries[0].EntryType.Should().Be(EmployeeLedgerEntryType.SalaryAccrual);
        entries[0].Amount.Should().Be(1200m, "the accrual credits the monthly salary");
    }

    [Fact]
    public async Task AdvancingClock_AccruesTheNextPeriod()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var employeeId = await SeedEmployeeAsync(scope, monthlySalary: 900m, active: true);

        var clock = new FakeClock(new DateTimeOffset(2099, 6, 1, 6, 0, 0, TimeSpan.Zero));
        await using var factory = new VetApiFactory { Clock = clock };

        await RunJobAsync(factory);
        clock.UtcNow = new DateTimeOffset(2099, 7, 1, 6, 0, 0, TimeSpan.Zero);
        await RunJobAsync(factory);

        await using var db = NewContext(scope);
        async Task<int> CountForPeriod(string period) => await db.EmployeeLedgerEntries.IgnoreQueryFilters()
            .CountAsync(e => e.EnvironmentId == scope.EnvironmentId
                             && e.IdempotencyKey == $"salary-accrual-{employeeId}-{period}");

        (await CountForPeriod("209906")).Should().Be(1, "June accrues once");
        (await CountForPeriod("209907")).Should().Be(1, "advancing to July accrues the next period");
    }

    [Fact]
    public async Task InactiveEmployee_IsNotAccrued()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var employeeId = await SeedEmployeeAsync(scope, monthlySalary: 1000m, active: false);

        var clock = new FakeClock(new DateTimeOffset(2099, 9, 10, 6, 0, 0, TimeSpan.Zero));
        await using var factory = new VetApiFactory { Clock = clock };
        await RunJobAsync(factory);

        await using var db = NewContext(scope);
        var ledgerId = await db.EmployeeLedgers.IgnoreQueryFilters()
            .Where(l => l.EmployeeId == employeeId).Select(l => l.Id).FirstAsync();
        (await db.EmployeeLedgerEntries.IgnoreQueryFilters().CountAsync(e => e.EmployeeLedgerId == ledgerId))
            .Should().Be(0, "an inactive employee never accrues salary");
    }

    private static async Task RunJobAsync(VetApiFactory factory)
    {
        using var serviceScope = factory.Services.CreateScope();
        var job = serviceScope.ServiceProvider.GetRequiredService<MonthlySalaryAccrualJob>();
        await job.RunAsync(CancellationToken.None);
    }

    private static ApplicationDbContext NewContext(PgTestScope scope) =>
        scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

    private static async Task<Guid> SeedEmployeeAsync(PgTestScope scope, decimal monthlySalary, bool active)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var now = DateTimeOffset.UtcNow;

        var employee = new Employee
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            FullName = "Salaried Worker",
            MonthlySalary = monthlySalary,
            Active = active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Employees.Add(employee);

        db.EmployeeLedgers.Add(new EmployeeLedger
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            EmployeeId = employee.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return employee.Id;
    }
}
