using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// PowerSync write path for <c>/sync/customers</c> (M3). A PUT also creates the customer's
/// <c>ledgers</c> row in the same transaction so field-doctor-authored customers stay
/// usable for receipts and invoices as soon as they sync. Field doctors may only set
/// <c>assigned_doctor_id</c> to themselves; admins / accountants / receptionists may set
/// it freely (M3 task 9).
/// </summary>
public sealed class CustomersSyncHandler : ISyncTableHandler
{
    public const string TableName = "customers";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public CustomersSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("customer_already_exists",
                $"Customer '{id}' already exists; use PATCH to update.");
        }

        var customer = new Customer
        {
            Id = id,
            Type = SyncBody.RequireString(body, "type", CustomerType.All, TableName),
            FullName = SyncBody.RequireString(body, "full_name"),
            PhonePrimary = SyncBody.OptionalString(body, "phone_primary"),
            PhoneSecondary = SyncBody.OptionalString(body, "phone_secondary"),
            Address = SyncBody.OptionalString(body, "address"),
            Email = SyncBody.OptionalString(body, "email"),
            IdNumber = SyncBody.OptionalString(body, "id_number"),
            Notes = SyncBody.OptionalString(body, "notes"),
            AssignedDoctorId = SyncBody.OptionalGuid(body, "assigned_doctor_id"),
        };

        EnforceAssignedDoctorPolicy(customer.AssignedDoctorId);

        _db.Customers.Add(customer);

        // M3 task 5 — the matching ledgers row goes in the same SaveChanges call.
        _db.Ledgers.Add(new Ledger
        {
            CustomerId = customer.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
        });

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(customer.Id, customer.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException(TableName, id);

        if (SyncBody.TryGetString(body, "type", out var type))
        {
            if (!CustomerType.All.Contains(type!))
            {
                throw new ConflictException("invalid_customer_type",
                    $"type '{type}' is not valid for customers.");
            }
            customer.Type = type!;
        }

        if (SyncBody.TryGetString(body, "full_name", out var fullName)) customer.FullName = fullName!;
        if (SyncBody.TryGetString(body, "phone_primary", out var p1)) customer.PhonePrimary = p1;
        if (SyncBody.TryGetString(body, "phone_secondary", out var p2)) customer.PhoneSecondary = p2;
        if (SyncBody.TryGetString(body, "address", out var addr)) customer.Address = addr;
        if (SyncBody.TryGetString(body, "email", out var email)) customer.Email = email;
        if (SyncBody.TryGetString(body, "id_number", out var idn)) customer.IdNumber = idn;
        if (SyncBody.TryGetString(body, "notes", out var notes)) customer.Notes = notes;

        if (body.TryGetProperty("assigned_doctor_id", out _))
        {
            var assigned = SyncBody.OptionalGuid(body, "assigned_doctor_id");
            EnforceAssignedDoctorPolicy(assigned);
            customer.AssignedDoctorId = assigned;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(customer.Id, customer.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException(TableName, id);

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(customer.Id, customer.UpdatedAt);
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    /// <summary>
    /// Field doctors may only assign customers to themselves. Admin / accountant / receptionist
    /// roles may assign freely. SCHEMA "Key invariants" #6 keeps the env in sync; this is the
    /// finer-grained per-doctor scoping called out in PRD §8.6.
    /// </summary>
    private void EnforceAssignedDoctorPolicy(Guid? assignedDoctorId)
    {
        if (!IsFieldDoctor(_user.Role))
        {
            return;
        }

        if (assignedDoctorId is { } target && target != _user.UserId)
        {
            throw new ForbiddenException("assigned_doctor_forbidden",
                "Field doctors may only assign customers to themselves.");
        }
    }

    private static bool IsFieldDoctor(string? role) =>
        role == RoleKey.VetField || role == RoleKey.VetBoth;
}
