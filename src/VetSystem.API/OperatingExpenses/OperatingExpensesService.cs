using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.OperatingExpenses.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.OperatingExpenses;

/// <summary>
/// CRUD for operating expenses (water, electricity, rent, …). Online-only center-web (admin/accountant),
/// env-scoped by the global query filter. Incurred expenses feed the clinic-profit report's net-profit
/// (subtracted by period) and, when unpaid, the "amount owed to others" total.
/// </summary>
public sealed class OperatingExpensesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;

    public OperatingExpensesService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OperatingExpenseResponse>> ListAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        bool? paid,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (category is not null && !OperatingExpenseCategory.All.Contains(category))
        {
            throw new ConflictException("invalid_category", $"category '{category}' is not valid.");
        }

        var query = _db.OperatingExpenses.AsNoTracking();

        if (category is not null) query = query.Where(e => e.Category == category);
        if (from is { } f) query = query.Where(e => e.IncurredOn >= f);
        if (to is { } t) query = query.Where(e => e.IncurredOn <= t);
        if (paid is { } p) query = query.Where(e => e.Paid == p);

        return await query
            .OrderByDescending(e => e.IncurredOn)
            .ThenByDescending(e => e.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .Select(e => new OperatingExpenseResponse(
                e.Id, e.Category, e.Amount, e.IncurredOn, e.Paid, e.PaidAt, e.Note, e.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OperatingExpenseResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var e = await _db.OperatingExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new NotFoundException("operating_expense", id);
        return ToResponse(e);
    }

    public async Task<OperatingExpenseResponse> CreateAsync(
        CreateOperatingExpenseRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();

        if (request.Id is { } id && id != Guid.Empty
            && await _db.OperatingExpenses.IgnoreQueryFilters().AnyAsync(e => e.Id == id, cancellationToken))
        {
            throw new ConflictException("operating_expense_id_collision", $"An expense with id '{id}' already exists.");
        }

        var entity = new OperatingExpense
        {
            Id = request.Id ?? Guid.Empty,
            Category = request.Category,
            Amount = request.Amount,
            IncurredOn = request.IncurredOn,
            Paid = request.Paid,
            PaidAt = request.Paid ? _clock.UtcNow : null,
            Note = request.Note,
            RecordedBy = userId,
        };

        _db.OperatingExpenses.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<OperatingExpenseResponse> UpdateAsync(
        Guid id, UpdateOperatingExpenseRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.OperatingExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new NotFoundException("operating_expense", id);

        if (request.Category is not null) entity.Category = request.Category;
        if (request.Amount is { } amount) entity.Amount = amount;
        if (request.IncurredOn is { } incurredOn) entity.IncurredOn = incurredOn;
        if (request.Note is not null) entity.Note = request.Note;
        if (request.Paid is { } paid && paid != entity.Paid)
        {
            entity.Paid = paid;
            entity.PaidAt = paid ? _clock.UtcNow : null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.OperatingExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new NotFoundException("operating_expense", id);

        _db.OperatingExpenses.Remove(entity); // AuditingInterceptor converts Deleted → soft-delete.
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static OperatingExpenseResponse ToResponse(OperatingExpense e) =>
        new(e.Id, e.Category, e.Amount, e.IncurredOn, e.Paid, e.PaidAt, e.Note, e.CreatedAt);

    private Guid RequireUser() =>
        _currentUser.UserId ?? throw new ForbiddenException("unauthenticated", "Authentication required.");
}
