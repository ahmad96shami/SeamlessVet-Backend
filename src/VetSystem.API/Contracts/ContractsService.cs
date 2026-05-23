using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Contracts;

/// <summary>
/// Contract authoring + lifecycle (PRD §6.6, M8 tasks 2–6). A <c>draft</c> is freely editable by any
/// holder of <c>contracts.write</c> (the field doctor authoring offline, or Admin/Accountant). The
/// binding transitions — creating directly as <c>active</c>, editing an already-<c>active</c>
/// contract's terms, completing it, or cancelling an active one — additionally require
/// <c>contracts.activate</c> (the activation gate, SCHEMA "Key invariants" #7). The
/// <see cref="POST"/> <c>/contracts/{id}/activate</c> endpoint being online-only is what satisfies the
/// "server-confirmed" half of the gate; the sync path rejects the <c>draft → active</c> edge entirely.
/// </summary>
public sealed class ContractsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IPermissionResolver _permissions;
    private readonly IContractLifecycleService _lifecycle;
    private readonly IMapper _mapper;
    private readonly IClock _clock;

    public ContractsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IPermissionResolver permissions,
        IContractLifecycleService lifecycle,
        IMapper mapper,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _permissions = permissions;
        _lifecycle = lifecycle;
        _mapper = mapper;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ContractResponse>> ListAsync(
        Guid? customerId,
        Guid? responsibleDoctorId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (status is not null && !ContractStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_contract_status", $"status '{status}' is not valid.");
        }

        var query = _db.Contracts.AsNoTracking();

        if (customerId is { } cid) query = query.Where(c => c.CustomerId == cid);
        if (responsibleDoctorId is { } did) query = query.Where(c => c.ResponsibleDoctorId == did);
        if (status is not null) query = query.Where(c => c.Status == status);

        var rows = await query
            .OrderByDescending(c => c.PeriodStart)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ContractResponse>).ToList();
    }

    public async Task<ContractResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("contract", id);

        return _mapper.Map<ContractResponse>(contract);
    }

    public async Task<ContractResponse> CreateAsync(ContractCreateRequest request, CancellationToken cancellationToken)
    {
        var (_, userId) = RequireUser();

        var status = request.Status ?? ContractStatus.Draft;

        // Creating a contract directly as `active` is itself an activation — gate it (PRD §6.6).
        if (status == ContractStatus.Active)
        {
            await RequireActivatePermissionAsync(cancellationToken);
        }

        await RequireExistsAsync(_db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken),
            "customer", request.CustomerId);

        // Defaults to the authoring user when omitted (the field-doctor flow; PRD §6.6).
        var responsibleDoctorId = request.ResponsibleDoctorId ?? userId;
        await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == responsibleDoctorId, cancellationToken),
            "doctor", responsibleDoctorId);

        if (request.Id is { } id && id != Guid.Empty
            && await _db.Contracts.IgnoreQueryFilters().AnyAsync(c => c.Id == id, cancellationToken))
        {
            throw new ConflictException("contract_id_collision", $"A contract with id '{id}' already exists.");
        }

        var now = _clock.UtcNow;
        var contract = new Contract
        {
            Id = request.Id ?? Guid.Empty,
            CustomerId = request.CustomerId,
            ResponsibleDoctorId = responsibleDoctorId,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            TotalPrice = request.TotalPrice,
            ExpectedVisitCount = request.ExpectedVisitCount,
            AnimalType = request.AnimalType,
            AnimalCount = request.AnimalCount,
            Status = status,
            CreatedBy = userId,
            ActivatedBy = status == ContractStatus.Active ? userId : null,
            ActivatedAt = status == ContractStatus.Active ? now : null,
        };

        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ContractResponse>(contract);
    }

    public async Task<ContractResponse> UpdateAsync(Guid id, ContractPatchRequest request, CancellationToken cancellationToken)
    {
        RequireUser();

        var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("contract", id);

        if (contract.Status is ContractStatus.Completed or ContractStatus.Cancelled)
        {
            throw new ConflictException("contract_terminal",
                $"Contract '{id}' is {contract.Status}; terminal contracts cannot be edited.");
        }

        // Binding terms on an active contract are server-authoritative — only an actor who can
        // activate (Admin/Accountant) may change them (PRD §6.6 lifecycle table).
        if (contract.Status == ContractStatus.Active)
        {
            await RequireActivatePermissionAsync(cancellationToken);
        }

        if (request.ResponsibleDoctorId is { } doctorId)
        {
            await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == doctorId, cancellationToken), "doctor", doctorId);
            contract.ResponsibleDoctorId = doctorId;
        }

        if (request.PeriodStart.HasValue) contract.PeriodStart = request.PeriodStart.Value;
        if (request.PeriodEnd.HasValue) contract.PeriodEnd = request.PeriodEnd;
        if (request.TotalPrice.HasValue) contract.TotalPrice = request.TotalPrice;
        if (request.ExpectedVisitCount.HasValue) contract.ExpectedVisitCount = request.ExpectedVisitCount;
        if (request.AnimalType is not null) contract.AnimalType = request.AnimalType;
        if (request.AnimalCount.HasValue) contract.AnimalCount = request.AnimalCount;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ContractResponse>(contract);
    }

    /// <summary>
    /// Draft → Active (M8 task 4). The endpoint is gated on <c>contracts.activate</c> and is online-only,
    /// satisfying both halves of the activation gate; here we stamp <c>activated_by</c>/<c>activated_at</c>
    /// and lock the terms. Idempotent: re-activating an already-active contract returns it unchanged.
    /// </summary>
    public async Task<ContractResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, userId) = RequireUser();

        var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("contract", id);

        if (contract.Status == ContractStatus.Active)
        {
            return _mapper.Map<ContractResponse>(contract);
        }

        _lifecycle.EnsureCanTransition(contract.Status, ContractStatus.Active);

        contract.Status = ContractStatus.Active;
        contract.ActivatedBy = userId;
        contract.ActivatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ContractResponse>(contract);
    }

    public Task<ContractResponse> CompleteAsync(Guid id, CancellationToken cancellationToken)
        => CloseAsync(id, ContractStatus.Completed, cancellationToken);

    public Task<ContractResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
        => CloseAsync(id, ContractStatus.Cancelled, cancellationToken);

    private async Task<ContractResponse> CloseAsync(Guid id, string target, CancellationToken cancellationToken)
    {
        RequireUser();

        var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("contract", id);

        // Idempotent: re-closing to the same terminal state is a no-op (offline replay safety).
        if (contract.Status == target)
        {
            return _mapper.Map<ContractResponse>(contract);
        }

        _lifecycle.EnsureCanTransition(contract.Status, target);

        // Changing the lifecycle of an already-binding (active) contract is an Admin/Accountant action;
        // cancelling a draft is fine with plain contracts.write (PRD §6.6 lifecycle table).
        if (contract.Status == ContractStatus.Active)
        {
            await RequireActivatePermissionAsync(cancellationToken);
        }

        contract.Status = target;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ContractResponse>(contract);
    }

    private async Task RequireActivatePermissionAsync(CancellationToken cancellationToken)
    {
        var (envId, userId) = RequireUser();
        var perms = await _permissions.ResolveAsync(userId, envId, cancellationToken);
        if (!perms.Contains(PermissionKey.ContractsActivate))
        {
            throw new ForbiddenException("missing_permission",
                $"Required permission '{PermissionKey.ContractsActivate}' is not granted.");
        }
    }

    private (Guid EnvironmentId, Guid UserId) RequireUser()
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return (envId, userId);
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
