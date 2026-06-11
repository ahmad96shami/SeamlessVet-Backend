using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Contracts;

/// <summary>
/// Contract↔farm attachment (M15 task 7), nested under a contract. A contract covers one-or-more
/// farms of the <b>same</b> owning customer (<see cref="Contract.CustomerId"/>); attaching a farm of a
/// different customer is rejected. Mutations are allowed only while the parent contract is
/// <c>draft</c> — once active its terms (including farm coverage) are server-authoritative and locked.
/// The unique <c>(contract, farm)</c> pair is enforced.
/// </summary>
public sealed class ContractFarmsService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public ContractFarmsService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ContractFarmResponse>> ListAsync(Guid contractId, CancellationToken cancellationToken)
    {
        await EnsureContractExistsAsync(contractId, cancellationToken);

        var rows = await _db.ContractFarms.AsNoTracking()
            .Where(cf => cf.ContractId == contractId)
            .OrderBy(cf => cf.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ContractFarmResponse>).ToList();
    }

    public async Task<ContractFarmResponse> AttachAsync(
        Guid contractId, ContractFarmAttachRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        var contractCustomerId = await RequireDraftContractAsync(contractId, cancellationToken);

        await RequireSameCustomerFarmAsync(request.FarmId, contractCustomerId, cancellationToken);

        if (request.Id is { } id && id != Guid.Empty
            && await _db.ContractFarms.IgnoreQueryFilters().AnyAsync(cf => cf.Id == id, cancellationToken))
        {
            throw new ConflictException("contract_farm_id_collision",
                $"A contract farm with id '{id}' already exists.");
        }

        if (await _db.ContractFarms.AnyAsync(
                cf => cf.ContractId == contractId && cf.FarmId == request.FarmId, cancellationToken))
        {
            throw new ConflictException("contract_farm_exists",
                "This farm is already attached to the contract.");
        }

        var entity = new ContractFarm
        {
            Id = request.Id ?? Guid.Empty,
            ContractId = contractId,
            FarmId = request.FarmId,
        };

        _db.ContractFarms.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ContractFarmResponse>(entity);
    }

    public async Task DetachAsync(Guid contractId, Guid farmId, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireDraftContractAsync(contractId, cancellationToken);

        var entity = await _db.ContractFarms
            .FirstOrDefaultAsync(cf => cf.ContractId == contractId && cf.FarmId == farmId, cancellationToken)
            ?? throw new NotFoundException("contract_farm", farmId);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.ContractFarms.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Returns the contract's owning customer id once it is confirmed to be a draft.</summary>
    private async Task<Guid> RequireDraftContractAsync(Guid contractId, CancellationToken cancellationToken)
    {
        var contract = await _db.Contracts
            .Where(c => c.Id == contractId)
            .Select(c => new { c.Status, c.CustomerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("contract", contractId);

        if (contract.Status != ContractStatus.Draft)
        {
            throw new ConflictException("contract_not_draft",
                $"Contract '{contractId}' is {contract.Status}; farms can only be attached/detached while the contract is draft.");
        }

        return contract.CustomerId;
    }

    private async Task RequireSameCustomerFarmAsync(Guid farmId, Guid contractCustomerId, CancellationToken cancellationToken)
    {
        var farmCustomerId = await _db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => (Guid?)f.CustomerId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("farm", farmId);

        if (farmCustomerId != contractCustomerId)
        {
            throw new ConflictException("contract_farm_customer_mismatch",
                "The farm belongs to a different customer than the contract; a contract covers only its own customer's farms.");
        }
    }

    private async Task EnsureContractExistsAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (!await _db.Contracts.AnyAsync(c => c.Id == contractId, cancellationToken))
        {
            throw new NotFoundException("contract", contractId);
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
