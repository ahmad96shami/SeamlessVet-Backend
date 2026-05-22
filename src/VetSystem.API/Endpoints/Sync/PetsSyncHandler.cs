using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// PowerSync write path for <c>/sync/pets</c> (M3). Pets always belong to a customer; the
/// sync handler verifies the target customer exists in the actor's environment via the global
/// query filter on every read.
/// </summary>
public sealed class PetsSyncHandler : ISyncTableHandler
{
    public const string TableName = "pets";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public PetsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.Pets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("pet_already_exists",
                $"Pet '{id}' already exists; use PATCH to update.");
        }

        var customerId = SyncBody.RequireGuid(body, "customer_id");
        await EnsureCustomerExistsAsync(customerId, cancellationToken);

        var sex = SyncBody.OptionalString(body, "sex");
        if (sex is not null && !PetSex.All.Contains(sex))
        {
            throw new ConflictException("invalid_pet_sex", $"sex '{sex}' is not valid.");
        }

        var pet = new Pet
        {
            Id = id,
            CustomerId = customerId,
            Name = SyncBody.RequireString(body, "name"),
            Species = SyncBody.OptionalString(body, "species"),
            Breed = SyncBody.OptionalString(body, "breed"),
            Sex = sex,
            DateOfBirth = SyncBody.OptionalDate(body, "date_of_birth"),
            ColorMarks = SyncBody.OptionalString(body, "color_marks"),
            WeightLatest = SyncBody.OptionalDecimal(body, "weight_latest"),
            PhotoUrl = SyncBody.OptionalString(body, "photo_url"),
            MicrochipNo = SyncBody.OptionalString(body, "microchip_no"),
            HealthNotes = SyncBody.OptionalString(body, "health_notes"),
        };

        _db.Pets.Add(pet);
        await _db.SaveChangesAsync(cancellationToken);

        return new SyncWriteResult(pet.Id, pet.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var pet = await _db.Pets.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                  ?? throw new NotFoundException(TableName, id);

        if (body.TryGetProperty("customer_id", out _))
        {
            var newOwner = SyncBody.RequireGuid(body, "customer_id");
            if (newOwner != pet.CustomerId)
            {
                await EnsureCustomerExistsAsync(newOwner, cancellationToken);
                pet.CustomerId = newOwner;
            }
        }

        if (SyncBody.TryGetString(body, "name", out var name)) pet.Name = name!;
        if (SyncBody.TryGetString(body, "species", out var species)) pet.Species = species;
        if (SyncBody.TryGetString(body, "breed", out var breed)) pet.Breed = breed;

        if (SyncBody.TryGetString(body, "sex", out var sex))
        {
            if (sex is not null && !PetSex.All.Contains(sex))
            {
                throw new ConflictException("invalid_pet_sex", $"sex '{sex}' is not valid.");
            }
            pet.Sex = sex;
        }

        if (body.TryGetProperty("date_of_birth", out _)) pet.DateOfBirth = SyncBody.OptionalDate(body, "date_of_birth");
        if (SyncBody.TryGetString(body, "color_marks", out var marks)) pet.ColorMarks = marks;
        if (body.TryGetProperty("weight_latest", out _)) pet.WeightLatest = SyncBody.OptionalDecimal(body, "weight_latest");
        if (SyncBody.TryGetString(body, "photo_url", out var photo)) pet.PhotoUrl = photo;
        if (SyncBody.TryGetString(body, "microchip_no", out var chip)) pet.MicrochipNo = chip;
        if (SyncBody.TryGetString(body, "health_notes", out var notes)) pet.HealthNotes = notes;

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(pet.Id, pet.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var pet = await _db.Pets.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                  ?? throw new NotFoundException(TableName, id);

        _db.Pets.Remove(pet);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(pet.Id, pet.UpdatedAt);
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
