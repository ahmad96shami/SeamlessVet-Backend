using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/contract_medication_prices</c> (M8 task 12). A medication price's authority follows its
/// parent contract: while the contract is <c>draft</c> it is doctor-device authoritative (the doctor
/// authors per-medication prices offline); once the contract is <c>active</c>+ the terms are
/// server-authoritative and writes are rejected. The unique <c>(contract, product)</c> pair is enforced.
/// </summary>
public sealed class ContractMedicationPricesSyncHandler : ISyncTableHandler
{
    public const string TableName = "contract_medication_prices";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ContractMedicationPricesSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        if (await _db.ContractMedicationPrices.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new ConflictException("contract_medication_price_already_exists",
                $"Contract medication price '{id}' already exists; use PATCH to update.");
        }

        var contractId = SyncBody.RequireGuid(body, "contract_id");
        await RequireDraftContractAsync(contractId, cancellationToken);

        var productId = SyncBody.RequireGuid(body, "product_id");
        await EnsureExistsAsync(_db.Products.AnyAsync(p => p.Id == productId, cancellationToken), "product", productId);

        if (await _db.ContractMedicationPrices.AnyAsync(
                p => p.ContractId == contractId && p.ProductId == productId, cancellationToken))
        {
            throw new ConflictException("contract_medication_price_exists",
                "This product already has a contract price; PATCH the existing one instead.");
        }

        var entity = new ContractMedicationPrice
        {
            Id = id,
            ContractId = contractId,
            ProductId = productId,
            ContractPrice = SyncBody.RequireDecimal(body, "contract_price"),
        };

        _db.ContractMedicationPrices.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(entity.Id, entity.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var price = await _db.ContractMedicationPrices.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        await RequireDraftContractAsync(price.ContractId, cancellationToken);

        if (body.TryGetProperty("contract_price", out _))
        {
            price.ContractPrice = SyncBody.RequireDecimal(body, "contract_price");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(price.Id, price.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var price = await _db.ContractMedicationPrices.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        await RequireDraftContractAsync(price.ContractId, cancellationToken);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.ContractMedicationPrices.Remove(price);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(price.Id, price.UpdatedAt);
    }

    private async Task RequireDraftContractAsync(Guid contractId, CancellationToken cancellationToken)
    {
        var status = await _db.Contracts
            .Where(c => c.Id == contractId)
            .Select(c => (string?)c.Status)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("contract", contractId);

        if (status != ContractStatus.Draft)
        {
            throw new ConflictException("contract_server_authoritative",
                $"Contract '{contractId}' is {status} on the server; its medication prices are locked. Re-pull the authoritative copy.");
        }
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
