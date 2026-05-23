using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Contracts;

/// <summary>
/// Per-medication contract price CRUD (PRD §6.6, M8 task 7), nested under a contract. Mutations are
/// allowed only while the parent contract is <c>draft</c> — once active, its terms (including
/// medication prices) are server-authoritative and locked. The unique <c>(contract, product)</c> pair
/// is enforced; a second price for the same product must be a PATCH, not a new row.
/// </summary>
public sealed class ContractMedicationPricesService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public ContractMedicationPricesService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ContractMedicationPriceResponse>> ListAsync(Guid contractId, CancellationToken cancellationToken)
    {
        await EnsureContractExistsAsync(contractId, cancellationToken);

        var rows = await _db.ContractMedicationPrices.AsNoTracking()
            .Where(p => p.ContractId == contractId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ContractMedicationPriceResponse>).ToList();
    }

    public async Task<ContractMedicationPriceResponse> CreateAsync(
        Guid contractId, ContractMedicationPriceCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireDraftContractAsync(contractId, cancellationToken);

        await RequireExistsAsync(_db.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken),
            "product", request.ProductId);

        if (request.Id is { } id && id != Guid.Empty
            && await _db.ContractMedicationPrices.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new ConflictException("contract_medication_price_id_collision",
                $"A contract medication price with id '{id}' already exists.");
        }

        if (await _db.ContractMedicationPrices.AnyAsync(
                p => p.ContractId == contractId && p.ProductId == request.ProductId, cancellationToken))
        {
            throw new ConflictException("contract_medication_price_exists",
                "This product already has a contract price; PATCH the existing one instead.");
        }

        var entity = new ContractMedicationPrice
        {
            Id = request.Id ?? Guid.Empty,
            ContractId = contractId,
            ProductId = request.ProductId,
            ContractPrice = request.ContractPrice,
        };

        _db.ContractMedicationPrices.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ContractMedicationPriceResponse>(entity);
    }

    public async Task<ContractMedicationPriceResponse> UpdateAsync(
        Guid contractId, Guid priceId, ContractMedicationPricePatchRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireDraftContractAsync(contractId, cancellationToken);

        var price = await _db.ContractMedicationPrices
            .FirstOrDefaultAsync(p => p.Id == priceId && p.ContractId == contractId, cancellationToken)
            ?? throw new NotFoundException("contract_medication_price", priceId);

        if (request.ContractPrice.HasValue) price.ContractPrice = request.ContractPrice.Value;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<ContractMedicationPriceResponse>(price);
    }

    public async Task DeleteAsync(Guid contractId, Guid priceId, CancellationToken cancellationToken)
    {
        RequireEnvironment();
        await RequireDraftContractAsync(contractId, cancellationToken);

        var price = await _db.ContractMedicationPrices
            .FirstOrDefaultAsync(p => p.Id == priceId && p.ContractId == contractId, cancellationToken)
            ?? throw new NotFoundException("contract_medication_price", priceId);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.ContractMedicationPrices.Remove(price);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureContractExistsAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (!await _db.Contracts.AnyAsync(c => c.Id == contractId, cancellationToken))
        {
            throw new NotFoundException("contract", contractId);
        }
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
            throw new ConflictException("contract_not_draft",
                $"Contract '{contractId}' is {status}; medication prices can only be changed while the contract is draft.");
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
