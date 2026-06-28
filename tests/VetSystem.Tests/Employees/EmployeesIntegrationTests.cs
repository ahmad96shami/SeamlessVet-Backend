using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Employees;

/// <summary>
/// M31 — the employee HR surface: CRUD (optional user link, one employee per linked user, ledger seeded
/// on create), payments (salary / loan / loan-repayment, the future-salary-deduction pairing,
/// idempotent), the statement signs, and the permission gates. The monthly salary accrual is covered by
/// <see cref="MonthlySalaryAccrualJobTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EmployeesIntegrationTests
{
    [Fact]
    public async Task Create_SeedsLedger_AllowsNonUserEmployee_AndEnforcesOnePerUser()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var staff = await SeedUserWithRoleAsync(scope, RoleKey.VetClinic, "Dr. Huda");
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        // A non-user employee (e.g. a janitor) is allowed — UserId is optional.
        var janitorId = await CreateEmployeeAsync(client, new { id = Guid.CreateVersion7(), userId = (Guid?)null, fullName = "Abu Ali", jobTitle = "Janitor", monthlySalary = 800m, active = true });
        var janitor = await client.GetFromJsonAsync<JsonElement>($"/employees/{janitorId}");
        janitor.GetProperty("userId").ValueKind.Should().Be(JsonValueKind.Null);
        janitor.GetProperty("balance").GetDecimal().Should().Be(0m);
        janitor.GetProperty("ledgerStatus").GetString().Should().Be(LedgerStatus.Open);

        // A user-linked employee seeds exactly one ledger.
        var linkedId = await CreateEmployeeAsync(client, new { id = Guid.CreateVersion7(), userId = staff.Id, fullName = "Dr. Huda", monthlySalary = 3000m, active = true });
        await using var db = NewContext(scope, admin.Id);
        (await db.EmployeeLedgers.AsNoTracking().CountAsync(l => l.EmployeeId == linkedId))
            .Should().Be(1, "exactly one ledger is seeded with the employee");

        // One employee per linked user.
        var dup = await PostAsync(client, "/employees", new { id = Guid.CreateVersion7(), userId = staff.Id, fullName = "Dr. Huda 2", monthlySalary = 3000m, active = true });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict, "a user can only map to one employee record");
    }

    [Fact]
    public async Task SalaryPayment_PostsNegativeEntry()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var employeeId = await CreateEmployeeAsync(client, NewEmployee(monthlySalary: 1000m));

        var payId = Guid.CreateVersion7();
        (await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = payId,
            kind = "salary_payment",
            amount = 500m,
            method = "cash",
            idempotencyKey = $"ep-{payId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var entry = await db.EmployeeLedgerEntries.AsNoTracking().SingleAsync(e => e.EmployeePaymentId == payId);
        entry.EntryType.Should().Be(EmployeeLedgerEntryType.SalaryPayment);
        entry.Amount.Should().Be(-500m, "a salary payment reduces the payable");
        entry.BalanceAfter.Should().Be(-500m);
    }

    [Fact]
    public async Task Loan_DebitsBalance()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var employeeId = await CreateEmployeeAsync(client, NewEmployee(monthlySalary: 1000m));

        var loanId = Guid.CreateVersion7();
        (await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = loanId,
            kind = "loan",
            amount = 300m,
            method = "cash",
            idempotencyKey = $"ep-{loanId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var entry = await db.EmployeeLedgerEntries.AsNoTracking().SingleAsync(e => e.EmployeePaymentId == loanId);
        entry.EntryType.Should().Be(EmployeeLedgerEntryType.Loan);
        entry.Amount.Should().Be(-300m, "a loan/advance drives the balance negative");
        (await BalanceAsync(db, employeeId)).Should().Be(-300m);
    }

    [Fact]
    public async Task Deduction_DebitsBalance()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var employeeId = await CreateEmployeeAsync(client, NewEmployee(monthlySalary: 1000m));

        var deductionId = Guid.CreateVersion7();
        (await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = deductionId,
            kind = "deduction",
            amount = 150m,
            method = "cash",
            idempotencyKey = $"ep-{deductionId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        var entry = await db.EmployeeLedgerEntries.AsNoTracking().SingleAsync(e => e.EmployeePaymentId == deductionId);
        entry.EntryType.Should().Be(EmployeeLedgerEntryType.Deduction);
        entry.Amount.Should().Be(-150m, "a خصم/deduction reduces the payable");
        (await BalanceAsync(db, employeeId)).Should().Be(-150m);
    }

    [Fact]
    public async Task FutureSalaryDeduction_Pairing_NetsToZero()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var employeeId = await CreateEmployeeAsync(client, NewEmployee(monthlySalary: 1000m));

        // 1) A 300 loan drives the balance to −300.
        var loanId = Guid.CreateVersion7();
        (await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = loanId, kind = "loan", amount = 300m, method = "cash", idempotencyKey = $"ep-{loanId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2) A month's salary accrues (+1000) → balance 700.
        await AccrueSalaryDirectlyAsync(scope, employeeId, 1000m);

        // 3) Pay the full 1000 salary, deducting 300 to repay the loan: salary_payment −1000 +
        //    loan_repayment +300 → balance 0, net cash handed over = 700.
        var payId = Guid.CreateVersion7();
        (await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = payId,
            kind = "salary_payment",
            amount = 1000m,
            loanRepaymentAmount = 300m,
            method = "cash",
            idempotencyKey = $"ep-{payId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await BalanceAsync(db, employeeId)).Should().Be(0m, "the deduction pairing nets the loan and salary to zero");

        var paired = await db.EmployeeLedgerEntries.AsNoTracking()
            .Where(e => e.EmployeePaymentId == payId).ToListAsync();
        paired.Should().HaveCount(2, "a deduction posts a salary_payment and a paired loan_repayment");
        paired.Single(e => e.EntryType == EmployeeLedgerEntryType.SalaryPayment).Amount.Should().Be(-1000m);
        paired.Single(e => e.EntryType == EmployeeLedgerEntryType.LoanRepayment).Amount.Should().Be(300m);

        var payment = await db.EmployeePayments.AsNoTracking().SingleAsync(p => p.Id == payId);
        payment.LoanRepaymentAmount.Should().Be(300m, "the withheld portion is recorded on the payment");
    }

    [Fact]
    public async Task Statement_SignsAreCorrect()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var employeeId = await CreateEmployeeAsync(client, NewEmployee(monthlySalary: 1000m));

        // Accrue 1000 (clinic owes), then pay 400 → closing balance +600.
        await AccrueSalaryDirectlyAsync(scope, employeeId, 1000m);
        var payId = Guid.CreateVersion7();
        (await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = payId, kind = "salary_payment", amount = 400m, method = "cash", idempotencyKey = $"ep-{payId:N}",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var statement = await client.GetFromJsonAsync<JsonElement>($"/employees/{employeeId}/statement");
        statement.GetProperty("employeeId").GetGuid().Should().Be(employeeId);
        statement.GetProperty("closingBalance").GetDecimal().Should().Be(600m, "+1000 accrual − 400 payment");
        statement.GetProperty("status").GetString().Should().Be(LedgerStatus.HasDebt, "the clinic still owes 600");

        var entries = statement.GetProperty("entries");
        entries.GetArrayLength().Should().Be(2);
        var amounts = entries.EnumerateArray().Select(e => e.GetProperty("amount").GetDecimal()).ToList();
        amounts.Should().Contain(1000m, "the accrual is a positive credit");
        amounts.Should().Contain(-400m, "the salary payment is a negative debit");
    }

    [Fact]
    public async Task Payment_IdempotentReplay_DoesNotDoublePost()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        await using var factory = new VetApiFactory();
        using var client = AuthedClient(factory, admin);

        var employeeId = await CreateEmployeeAsync(client, NewEmployee(monthlySalary: 1000m));
        var body = new
        {
            id = Guid.CreateVersion7(),
            kind = "salary_payment",
            amount = 250m,
            method = "cash",
            idempotencyKey = "stable-employee-payment-key",
        };
        var key = $"ep-hdr-{Guid.NewGuid():N}"[..32];

        (await PostAsync(client, $"/employees/{employeeId}/payments", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/employees/{employeeId}/payments", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewContext(scope, admin.Id);
        (await db.EmployeePayments.AsNoTracking().CountAsync(p => p.EmployeeId == employeeId))
            .Should().Be(1, "the same idempotency key collapses retries to one payment");
        (await BalanceAsync(db, employeeId)).Should().Be(-250m, "the single payment debits the ledger once");
    }

    [Fact]
    public async Task Manage_And_Pay_RequirePermission()
    {
        await using var scope = await PgTestScope.CreateAsync();
        var admin = await AdminTestSeed.SeedAdminAsync(scope);
        var fieldVet = await SeedUserWithRoleAsync(scope, RoleKey.VetField, "No-Perms"); // no employees.* granted
        await using var factory = new VetApiFactory();

        Guid employeeId;
        using (var adminClient = AuthedClient(factory, admin))
        {
            employeeId = await CreateEmployeeAsync(adminClient, NewEmployee(monthlySalary: 1000m));
        }

        using var client = AuthedClient(factory, fieldVet, role: RoleKey.VetField);
        var create = await PostAsync(client, "/employees", NewEmployee(monthlySalary: 500m));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden, "employees.manage gates employee writes");

        var pay = await PostAsync(client, $"/employees/{employeeId}/payments", new
        {
            id = Guid.CreateVersion7(), kind = "salary_payment", amount = 100m, method = "cash", idempotencyKey = "x",
        });
        pay.StatusCode.Should().Be(HttpStatusCode.Forbidden, "employees.pay gates payments");
    }

    // ---- helpers ----

    private static object NewEmployee(decimal monthlySalary) =>
        new { id = Guid.CreateVersion7(), userId = (Guid?)null, fullName = "Worker", monthlySalary, active = true };

    private static HttpClient AuthedClient(VetApiFactory factory, User user, string role = "admin")
    {
        var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<IJwtTokenService>()
            .IssueAccessToken(new UserPrincipal(user.Id, user.EnvironmentId, role));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body, string? idemKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idemKey ?? $"it-{Guid.NewGuid():N}"[..32]);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, object body)
    {
        var resp = await PostAsync(client, "/employees", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<decimal> BalanceAsync(ApplicationDbContext db, Guid employeeId) =>
        await db.EmployeeLedgers.AsNoTracking().Where(l => l.EmployeeId == employeeId).Select(l => l.Balance).FirstAsync();

    /// <summary>
    /// Posts a salary_accrual entry straight onto the employee ledger (the monthly job's effect), so a
    /// payment test has a credit to settle without running the env-wide accrual job.
    /// </summary>
    private static async Task AccrueSalaryDirectlyAsync(PgTestScope scope, Guid employeeId, decimal amount)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var now = DateTimeOffset.UtcNow;
        var ledger = await db.EmployeeLedgers.IgnoreQueryFilters().FirstAsync(l => l.EmployeeId == employeeId);
        var newBalance = ledger.Balance + amount;

        db.EmployeeLedgerEntries.Add(new EmployeeLedgerEntry
        {
            Id = Guid.CreateVersion7(),
            EnvironmentId = scope.EnvironmentId,
            EmployeeLedgerId = ledger.Id,
            EntryType = EmployeeLedgerEntryType.SalaryAccrual,
            Amount = amount,
            BalanceAfter = newBalance,
            IdempotencyKey = $"test-accrual-{Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
        });

        ledger.Balance = newBalance;
        ledger.Status = newBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;
        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid? userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });

    private static async Task<User> SeedUserWithRoleAsync(PgTestScope scope, string roleKey, string fullName)
    {
        await using var db = NewContext(scope, null);
        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EnvironmentId == scope.EnvironmentId && r.Key == roleKey);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = fullName,
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "x",
            Status = UserStatus.Active,
            NumberPrefix = $"E{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
