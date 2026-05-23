using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/contracts</c> (M8 task 11) — enforces the contract conflict rule (PRD §8.4, SCHEMA
/// "Key invariants" #5, #7). A <c>draft</c> is doctor-device authoritative: the device may insert,
/// edit its terms, abandon (cancel), or soft-delete it offline. Once the server's copy is
/// <c>active</c>/<c>completed</c>/<c>cancelled</c> it is server-authoritative and a conflicting client
/// write is rejected (the client re-pulls). The <c>draft → active</c> edge is refused on this path
/// entirely — activation requires connectivity and the <c>contracts.activate</c> permission, so it
/// only happens through <c>POST /contracts/{id}/activate</c> (PRD §8.9).
/// </summary>
public sealed class ContractsSyncHandler : ISyncTableHandler
{
    public const string TableName = "contracts";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IClock _clock;

    public ContractsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        var userId = RequireAuthenticated();

        if (await _db.Contracts.IgnoreQueryFilters().AnyAsync(c => c.Id == id, cancellationToken))
        {
            throw new ConflictException("contract_already_exists", $"Contract '{id}' already exists; use PATCH to update.");
        }

        // Contracts are born `draft` offline; promoting to active is online-only.
        var status = SyncBody.OptionalString(body, "status") ?? ContractStatus.Draft;
        if (status != ContractStatus.Draft)
        {
            throw ActivationRequiresOnline();
        }

        var customerId = SyncBody.RequireGuid(body, "customer_id");
        await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken), "customer", customerId);

        var responsibleDoctorId = SyncBody.OptionalGuid(body, "responsible_doctor_id") ?? userId;
        await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == responsibleDoctorId, cancellationToken), "doctor", responsibleDoctorId);

        var contract = new Contract
        {
            Id = id,
            CustomerId = customerId,
            ResponsibleDoctorId = responsibleDoctorId,
            PeriodStart = SyncBody.OptionalDate(body, "period_start")
                          ?? throw new ConflictException("invalid_payload", "'period_start' is required and must be a date."),
            PeriodEnd = SyncBody.OptionalDate(body, "period_end"),
            TotalPrice = SyncBody.OptionalDecimal(body, "total_price"),
            ExpectedVisitCount = SyncBody.OptionalInt(body, "expected_visit_count"),
            AnimalType = SyncBody.OptionalString(body, "animal_type"),
            AnimalCount = SyncBody.OptionalInt(body, "animal_count"),
            Status = ContractStatus.Draft,
            CreatedBy = SyncBody.OptionalGuid(body, "created_by") ?? userId,
        };

        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(contract.Id, contract.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException(TableName, id);

        // Server-wins: once active+, the binding terms are authoritative; the offline edit is rejected.
        if (ContractStatus.IsServerAuthoritative(contract.Status))
        {
            throw new ConflictException("contract_server_authoritative",
                $"Contract '{id}' is {contract.Status} on the server and cannot be changed offline; re-pull the authoritative copy.");
        }

        if (SyncBody.TryGetString(body, "status", out var statusValue) && statusValue is not null
            && statusValue != contract.Status)
        {
            if (statusValue == ContractStatus.Active)
            {
                throw ActivationRequiresOnline();
            }

            if (!ContractStatus.CanTransition(contract.Status, statusValue))
            {
                throw new ConflictException("invalid_contract_transition",
                    $"Cannot transition a contract from '{contract.Status}' to '{statusValue}'.");
            }

            // Only draft → cancelled reaches here (the doctor abandoning their own draft offline).
            contract.Status = statusValue;
        }

        if (body.TryGetProperty("responsible_doctor_id", out _)
            && SyncBody.OptionalGuid(body, "responsible_doctor_id") is { } did)
        {
            await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == did, cancellationToken), "doctor", did);
            contract.ResponsibleDoctorId = did;
        }

        ApplyDraftTerms(contract, body);

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(contract.Id, contract.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException(TableName, id);

        if (ContractStatus.IsServerAuthoritative(contract.Status))
        {
            throw new ConflictException("contract_server_authoritative",
                $"Contract '{id}' is {contract.Status} on the server and cannot be deleted.");
        }

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.Contracts.Remove(contract);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(contract.Id, contract.UpdatedAt);
    }

    private static void ApplyDraftTerms(Contract contract, JsonElement body)
    {
        if (body.TryGetProperty("period_start", out _) && SyncBody.OptionalDate(body, "period_start") is { } start)
        {
            contract.PeriodStart = start;
        }

        if (body.TryGetProperty("period_end", out _)) contract.PeriodEnd = SyncBody.OptionalDate(body, "period_end");
        if (body.TryGetProperty("total_price", out _)) contract.TotalPrice = SyncBody.OptionalDecimal(body, "total_price");
        if (body.TryGetProperty("expected_visit_count", out _)) contract.ExpectedVisitCount = SyncBody.OptionalInt(body, "expected_visit_count");
        if (SyncBody.TryGetString(body, "animal_type", out var animalType)) contract.AnimalType = animalType;
        if (body.TryGetProperty("animal_count", out _)) contract.AnimalCount = SyncBody.OptionalInt(body, "animal_count");
    }

    private static ConflictException ActivationRequiresOnline() => new(
        "contract_activation_requires_online",
        "Promoting a contract to active requires connectivity and the contracts.activate permission; "
        + "use POST /contracts/{id}/activate.");

    private Guid RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return userId;
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
