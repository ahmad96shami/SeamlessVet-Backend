using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Employees.Contracts;
using VetSystem.Application.EmployeeLedgers;
using VetSystem.Application.EmployeeLedgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Employees;

/// <summary>
/// Employee payments (M31) — the HR mirror of supplier payments. Issuing one records the cash event and
/// posts the matching <c>employee_ledger_entries</c> in one transaction:
/// <list type="bullet">
/// <item><c>salary_payment</c> → a negative <c>salary_payment</c> entry (reduces the payable). If a
/// <c>LoanRepaymentAmount</c> is supplied it also posts a positive <c>loan_repayment</c> entry — the
/// future-salary-deduction pairing — so the full salary is settled while the deducted portion repays the
/// loan and the net cash handed over is <c>Amount − LoanRepaymentAmount</c>.</item>
/// <item><c>loan</c> → a negative <c>loan</c> entry (an advance, driving the balance negative).</item>
/// <item><c>loan_repayment</c> → a positive <c>loan_repayment</c> entry (direct cash repayment).</item>
/// </list>
/// Append-only and idempotent per environment so a retried payment never double-posts.
/// </summary>
public sealed class EmployeePaymentsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IEmployeeLedgerService _ledgers;

    public EmployeePaymentsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IEmployeeLedgerService ledgers)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _ledgers = ledgers;
    }

    public async Task<EmployeePaymentResponse> IssueAsync(
        Guid employeeId, EmployeePaymentRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.EmployeePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EnvironmentId == envId && p.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<EmployeePaymentResponse>(replay);
        }

        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            throw new NotFoundException("employee", employeeId);
        }

        var ledgerId = await _db.EmployeeLedgers
            .Where(l => l.EmployeeId == employeeId)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("employee_ledger", employeeId);

        if (request.Id is { } rid && rid != Guid.Empty
            && await _db.EmployeePayments.IgnoreQueryFilters().AnyAsync(p => p.Id == rid, cancellationToken))
        {
            throw new ConflictException("employee_payment_id_collision",
                $"An employee payment with id '{rid}' already exists.");
        }

        var amount = Money(request.Amount);
        var deduction = request.Kind == EmployeePaymentKind.SalaryPayment
            ? Money(request.LoanRepaymentAmount ?? 0m)
            : 0m;
        if (deduction > amount)
        {
            throw new ConflictException("loan_deduction_exceeds_salary",
                "The loan deduction cannot exceed the salary being paid.");
        }

        var payment = new EmployeePayment
        {
            Id = request.Id ?? Guid.Empty,
            EmployeeId = employeeId,
            Kind = request.Kind,
            Amount = amount,
            LoanRepaymentAmount = deduction > 0m ? deduction : null,
            Method = request.Method,
            PaidBy = userId,
            PaidAt = _clock.UtcNow,
            Notes = request.Notes,
            ChequeNumber = request.ChequeNumber,
            ChequeBank = request.ChequeBank,
            ChequeDueDate = request.ChequeDueDate,
            IdempotencyKey = request.IdempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.EmployeePayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken); // assigns payment.Id

        // Primary ledger entry: a salary payment / loan debits the payable; a loan repayment credits it.
        var (primaryType, primaryAmount) = request.Kind switch
        {
            EmployeePaymentKind.SalaryPayment => (EmployeeLedgerEntryType.SalaryPayment, -payment.Amount),
            EmployeePaymentKind.Loan => (EmployeeLedgerEntryType.Loan, -payment.Amount),
            EmployeePaymentKind.LoanRepayment => (EmployeeLedgerEntryType.LoanRepayment, payment.Amount),
            // خصم — a deduction debits the payable (mirrors the salary_payment / loan sign).
            EmployeePaymentKind.Deduction => (EmployeeLedgerEntryType.Deduction, -payment.Amount),
            _ => throw new ConflictException("invalid_employee_payment_kind", $"Unknown kind '{request.Kind}'."),
        };

        await _ledgers.AppendEntryAsync(
            new EmployeeLedgerEntryRequest(
                Id: null,
                EmployeeLedgerId: ledgerId,
                EntryType: primaryType,
                Amount: primaryAmount,
                EmployeePaymentId: payment.Id,
                Description: payment.Notes ?? DefaultDescription(request.Kind),
                IdempotencyKey: $"employee-payment-{payment.Id}"),
            cancellationToken);

        // Future-salary-deduction pairing: the withheld portion repays the loan as a positive entry, so
        // the full salary is settled (−Amount) while the loan balance is reduced (+deduction).
        if (deduction > 0m)
        {
            await _ledgers.AppendEntryAsync(
                new EmployeeLedgerEntryRequest(
                    Id: null,
                    EmployeeLedgerId: ledgerId,
                    EntryType: EmployeeLedgerEntryType.LoanRepayment,
                    Amount: deduction,
                    EmployeePaymentId: payment.Id,
                    Description: "Loan repayment (salary deduction)",
                    IdempotencyKey: $"employee-loan-repayment-{payment.Id}"),
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return _mapper.Map<EmployeePaymentResponse>(payment);
    }

    public async Task<IReadOnlyList<EmployeePaymentResponse>> ListAsync(
        Guid employeeId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var rows = await _db.EmployeePayments.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.PaidAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<EmployeePaymentResponse>).ToList();
    }

    private static string DefaultDescription(string kind) => kind switch
    {
        EmployeePaymentKind.SalaryPayment => "Salary payment",
        EmployeePaymentKind.Loan => "Employee loan",
        EmployeePaymentKind.LoanRepayment => "Loan repayment",
        EmployeePaymentKind.Deduction => "Deduction",
        _ => "Employee payment",
    };

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
