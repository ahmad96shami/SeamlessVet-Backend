using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Employees.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Employees;

/// <summary>
/// M31 employee CRUD (SCHEMA §4). Online-only center-web (admin/accountant). The HR mirror of
/// <see cref="Supplier"/> on the AP side, with an <b>optional</b> user link. <see cref="CreateAsync"/>
/// validates the user (when supplied), enforces one employee per linked user, and seeds the matching
/// <see cref="EmployeeLedger"/> in the same transaction so the salary-accrual and payment flows always
/// have one to post into.
/// </summary>
public sealed class EmployeesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public EmployeesService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<EmployeeResponse>> ListAsync(
        string? search,
        string? ledgerStatus,
        bool? active,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (ledgerStatus is not null && !LedgerStatus.All.Contains(ledgerStatus))
        {
            throw new ConflictException("invalid_ledger_status", $"ledgerStatus '{ledgerStatus}' is not valid.");
        }

        var employees = _db.Employees.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            employees = employees.Where(e =>
                EF.Functions.ILike(e.FullName, pattern) ||
                (e.JobTitle != null && EF.Functions.ILike(e.JobTitle, pattern)));
        }

        if (active is { } isActive)
        {
            employees = employees.Where(e => e.Active == isActive);
        }

        if (ledgerStatus is { } status)
        {
            employees = employees.Where(e =>
                _db.EmployeeLedgers.Any(l => l.EmployeeId == e.Id && l.Status == status));
        }

        var page = await employees
            .OrderBy(e => e.FullName)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return [];
        }

        var ids = page.Select(e => e.Id).ToList();
        var ledgers = await _db.EmployeeLedgers.AsNoTracking()
            .Where(l => ids.Contains(l.EmployeeId))
            .ToDictionaryAsync(l => l.EmployeeId, l => new { l.Balance, l.Status }, cancellationToken);

        return page.Select(e =>
        {
            ledgers.TryGetValue(e.Id, out var ledger);
            return ToResponse(e, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
        }).ToList();
    }

    public async Task<EmployeeResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                       ?? throw new NotFoundException("employee", id);

        var ledger = await _db.EmployeeLedgers.AsNoTracking()
            .Where(l => l.EmployeeId == id)
            .Select(l => new { l.Balance, l.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return ToResponse(employee, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    public async Task<EmployeeResponse> CreateAsync(EmployeeRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.Id is { } id && id != Guid.Empty
            && await _db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == id, cancellationToken))
        {
            throw new ConflictException("employee_id_collision", $"An employee with id '{id}' already exists.");
        }

        if (request.UserId is { } userId)
        {
            if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            {
                throw new NotFoundException("user", userId);
            }

            if (await _db.Employees.AnyAsync(e => e.UserId == userId, cancellationToken))
            {
                throw new ConflictException("employee_user_taken",
                    "This user already has an employee record.");
            }
        }

        var entity = new Employee
        {
            Id = request.Id ?? Guid.Empty,
            UserId = request.UserId,
            FullName = request.FullName,
            JobTitle = request.JobTitle,
            MonthlySalary = Money(request.MonthlySalary),
            Active = request.Active,
            HiredAt = request.HiredAt,
            Notes = request.Notes,
        };

        _db.Employees.Add(entity);

        // One employee ledger is created in the same transaction (the AuditingInterceptor stamps
        // id + environment_id + timestamps), so the salary-accrual and payment flows always have one.
        var ledger = new EmployeeLedger
        {
            EmployeeId = entity.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
        };
        _db.EmployeeLedgers.Add(ledger);

        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(entity, ledger.Balance, ledger.Status);
    }

    public async Task<EmployeeResponse> UpdateAsync(
        Guid id, EmployeePatchRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new NotFoundException("employee", id);

        if (request.FullName is not null) entity.FullName = request.FullName;
        if (request.JobTitle is not null) entity.JobTitle = request.JobTitle;
        if (request.MonthlySalary is { } salary) entity.MonthlySalary = Money(salary);
        if (request.Active is { } active) entity.Active = active;
        if (request.HiredAt is not null) entity.HiredAt = request.HiredAt;
        if (request.Notes is not null) entity.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);

        var ledger = await _db.EmployeeLedgers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.EmployeeId == entity.Id, cancellationToken);
        return ToResponse(entity, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new NotFoundException("employee", id);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.Employees.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private EmployeeResponse ToResponse(Employee employee, decimal balance, string ledgerStatus) =>
        _mapper.Map<EmployeeResponse>(employee) with { Balance = balance, LedgerStatus = ledgerStatus };

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
