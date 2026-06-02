using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/contract_farms</c> (M15 task 9). A contract↔farm attachment's authority follows its
/// parent contract: while the contract is <c>draft</c> it is doctor-device authoritative (the doctor
/// attaches farms offline while authoring); once the contract is <c>active</c>+ the coverage is
/// server-authoritative and writes are rejected. The farm must belong to the contract's own customer,
/// and the unique <c>(contract, farm)</c> pair is enforced. Mirrors
/// <see cref="ContractMedicationPricesSyncHandler"/>.
/// </summary>
public sealed class ContractFarmsSyncHandler : ISyncTableHandler
{
    public const string TableName = "contract_farms";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ContractFarmsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        if (await _db.ContractFarms.IgnoreQueryFilters().AnyAsync(cf => cf.Id == id, cancellationToken))
        {
            throw new ConflictException("contract_farm_already_exists",
                $"Contract farm '{id}' already exists; use DELETE to detach.");
        }

        var contractId = SyncBody.RequireGuid(body, "contract_id");
        var contractCustomerId = await RequireDraftContractAsync(contractId, cancellationToken);

        var farmId = SyncBody.RequireGuid(body, "farm_id");
        await RequireSameCustomerFarmAsync(farmId, contractCustomerId, cancellationToken);

        if (await _db.ContractFarms.AnyAsync(
                cf => cf.ContractId == contractId && cf.FarmId == farmId, cancellationToken))
        {
            throw new ConflictException("contract_farm_exists",
                "This farm is already attached to the contract.");
        }

        var entity = new ContractFarm
        {
            Id = id,
            ContractId = contractId,
            FarmId = farmId,
        };

        _db.ContractFarms.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entity.Id, entity.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        // A contract↔farm attachment has no mutable columns (it is just the FK pair). PATCH only
        // re-validates the draft gate and is otherwise a no-op; attach/detach is PUT/DELETE.
        var entity = await _db.ContractFarms.FirstOrDefaultAsync(cf => cf.Id == id, cancellationToken)
                     ?? throw new NotFoundException(TableName, id);

        await RequireDraftContractAsync(entity.ContractId, cancellationToken);
        return new SyncWriteResult(entity.Id, entity.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var entity = await _db.ContractFarms.FirstOrDefaultAsync(cf => cf.Id == id, cancellationToken)
                     ?? throw new NotFoundException(TableName, id);

        await RequireDraftContractAsync(entity.ContractId, cancellationToken);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.ContractFarms.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entity.Id, entity.UpdatedAt);
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
            throw new ConflictException("contract_server_authoritative",
                $"Contract '{contractId}' is {contract.Status} on the server; its farm coverage is locked. Re-pull the authoritative copy.");
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

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
