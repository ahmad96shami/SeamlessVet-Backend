using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// PowerSync write path for <c>/sync/farms</c> (M15). Farms are attached to a customer exactly like
/// pets (last-write-wins, device-authoritative); the handler verifies the owning customer exists in
/// the actor's environment. A farm carries no doctor of its own — it inherits the customer's and
/// streams through the existing <c>by_customer</c> scope. Mirrors <see cref="PetsSyncHandler"/>.
/// </summary>
public sealed class FarmsSyncHandler : ISyncTableHandler
{
    public const string TableName = "farms";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public FarmsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.Farms
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("farm_already_exists",
                $"Farm '{id}' already exists; use PATCH to update.");
        }

        var customerId = SyncBody.RequireGuid(body, "customer_id");
        await EnsureCustomerExistsAsync(customerId, cancellationToken);

        var farm = new Farm
        {
            Id = id,
            CustomerId = customerId,
            Name = SyncBody.RequireString(body, "name"),
            Kind = SyncBody.RequireString(body, "kind", FarmKind.All, TableName),
            Location = SyncBody.OptionalString(body, "location"),
            AnimalType = SyncBody.OptionalString(body, "animal_type"),
            HeadCount = SyncBody.OptionalInt(body, "head_count"),
            Notes = SyncBody.OptionalString(body, "notes"),
        };

        _db.Farms.Add(farm);
        await _db.SaveChangesAsync(cancellationToken);

        return new SyncWriteResult(farm.Id, farm.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                   ?? throw new NotFoundException(TableName, id);

        if (body.TryGetProperty("customer_id", out _))
        {
            var newOwner = SyncBody.RequireGuid(body, "customer_id");
            if (newOwner != farm.CustomerId)
            {
                await EnsureCustomerExistsAsync(newOwner, cancellationToken);
                farm.CustomerId = newOwner;
            }
        }

        if (SyncBody.TryGetString(body, "name", out var name)) farm.Name = name!;
        if (SyncBody.TryGetString(body, "kind", out var kind))
        {
            if (kind is null || !FarmKind.All.Contains(kind))
            {
                throw new ConflictException("invalid_farm_kind", $"kind '{kind}' is not valid.");
            }
            farm.Kind = kind;
        }
        if (SyncBody.TryGetString(body, "location", out var location)) farm.Location = location;
        if (SyncBody.TryGetString(body, "animal_type", out var animalType)) farm.AnimalType = animalType;
        if (body.TryGetProperty("head_count", out _)) farm.HeadCount = SyncBody.OptionalInt(body, "head_count");
        if (SyncBody.TryGetString(body, "notes", out var notes)) farm.Notes = notes;

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(farm.Id, farm.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                   ?? throw new NotFoundException(TableName, id);

        _db.Farms.Remove(farm);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(farm.Id, farm.UpdatedAt);
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private async Task EnsureCustomerExistsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var exists = await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("customer", customerId);
        }
    }
}
