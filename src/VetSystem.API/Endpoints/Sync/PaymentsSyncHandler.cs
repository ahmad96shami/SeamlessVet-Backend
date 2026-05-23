using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/payments</c> (M7 tasks 11, 13) — append-only, server-wins. PUT persists a device's
/// payment leg against an existing invoice. PATCH/DELETE are rejected (a refund is a new row, not an
/// edit).
/// </summary>
public sealed class PaymentsSyncHandler : ISyncTableHandler
{
    public const string TableName = "payments";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IClock _clock;

    public PaymentsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.Payments.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing is not null)
        {
            return new SyncWriteResult(existing.Id, existing.UpdatedAt); // idempotent
        }

        var invoiceId = SyncBody.RequireGuid(body, "invoice_id");
        await EnsureExistsAsync(_db.Invoices.AnyAsync(i => i.Id == invoiceId, cancellationToken), "invoice", invoiceId);

        var amount = SyncBody.RequireDecimal(body, "amount");
        if (amount <= 0m)
        {
            throw new ConflictException("invalid_payment_amount", "payment amount must be greater than zero.");
        }

        var payment = new Payment
        {
            Id = id,
            InvoiceId = invoiceId,
            Method = SyncBody.RequireString(body, "method", PaymentMethod.All, TableName),
            Amount = amount,
            PaidAt = SyncBody.OptionalDateTime(body, "paid_at") ?? _clock.UtcNow,
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(payment.Id, payment.UpdatedAt);
    }

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw AppendOnly();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw AppendOnly();

    private static ConflictException AppendOnly() => new(
        "payments_append_only", "payments are append-only; post a compensating entry to correct one.");

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
