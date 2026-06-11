using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Jobs;

/// <summary>
/// M31 task 4 — monthly salary accrual. Once a month it posts a <c>salary_accrual</c> (+monthly_salary)
/// entry on every active employee's HR ledger, crediting what the clinic owes them. Like the other M11
/// jobs it runs without an HTTP principal, so it enumerates every environment explicitly
/// (<c>IgnoreQueryFilters</c>) and stamps <see cref="Entity.EnvironmentId"/> on the rows it writes, and it
/// reads "now" from <see cref="IClock"/> so a forced clock drives it deterministically in tests.
/// <para>
/// Exactly-once per period is guaranteed by the <b>period idempotency key</b>
/// <c>salary-accrual-{employeeId}-{yyyyMM}</c> on <c>employee_ledger_entries</c>: a pre-check skips an
/// already-accrued employee, and the unique <c>(environment_id, idempotency_key)</c> index makes a
/// concurrent re-run a safe no-op (the duplicate insert fails and is swallowed). Loans repay against this
/// running balance via the employee-payment flow (a salary deduction or direct cash) — out of scope here.
/// </para>
/// </summary>
public sealed class MonthlySalaryAccrualJob
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<MonthlySalaryAccrualJob> _logger;

    public MonthlySalaryAccrualJob(ApplicationDbContext db, IClock clock, ILogger<MonthlySalaryAccrualJob> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var period = _clock.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture);

        foreach (var environmentId in await JobHelpers.ActiveEnvironmentIdsAsync(_db, cancellationToken))
        {
            var employees = await _db.Employees
                .IgnoreQueryFilters()
                .Where(e => e.EnvironmentId == environmentId && e.DeletedAt == null && e.Active)
                .Select(e => new { e.Id, e.MonthlySalary })
                .ToListAsync(cancellationToken);

            var accrued = 0;
            foreach (var employee in employees)
            {
                if (employee.MonthlySalary <= 0m)
                {
                    continue;
                }

                if (await AccrueAsync(environmentId, employee.Id, employee.MonthlySalary, period, cancellationToken))
                {
                    accrued++;
                }
            }

            if (accrued > 0)
            {
                _logger.LogInformation(
                    "Accrued salary for {Count} employees in environment {EnvironmentId} for period {Period}",
                    accrued, environmentId, period);
            }
        }
    }

    private async Task<bool> AccrueAsync(
        Guid environmentId, Guid employeeId, decimal monthlySalary, string period, CancellationToken cancellationToken)
    {
        var idempotencyKey = $"salary-accrual-{employeeId}-{period}";

        // Pre-check: exactly-once per period. The unique index below is the concurrency backstop.
        var alreadyAccrued = await _db.EmployeeLedgerEntries
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EnvironmentId == environmentId && e.IdempotencyKey == idempotencyKey, cancellationToken);
        if (alreadyAccrued)
        {
            return false;
        }

        var ledger = await _db.EmployeeLedgers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.EmployeeId == employeeId, cancellationToken);
        if (ledger is null || ledger.Status == LedgerStatus.Closed)
        {
            return false;
        }

        var newBalance = ledger.Balance + monthlySalary;

        _db.EmployeeLedgerEntries.Add(new EmployeeLedgerEntry
        {
            EnvironmentId = environmentId,
            EmployeeLedgerId = ledger.Id,
            EntryType = EmployeeLedgerEntryType.SalaryAccrual,
            Amount = monthlySalary,
            BalanceAfter = newBalance,
            Description = $"Salary accrual {period[..4]}-{period[4..]}",
            IdempotencyKey = idempotencyKey,
        });

        ledger.Balance = newBalance;
        ledger.Status = newBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            // A concurrent run won the period key; nothing persisted here. Discard the in-memory ledger
            // bump so the next employee isn't computed off a stale balance.
            await _db.Entry(ledger).ReloadAsync(cancellationToken);
            return false;
        }
    }

    private static bool IsIdempotencyViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg
        && pg.SqlState == "23505"
        && pg.ConstraintName == "ux_employee_ledger_entries_env_idempotency";
}
