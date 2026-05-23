using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/receipt_vouchers</c> (M7 tasks 11, 13) — append-only, server-wins. PUT persists a
/// device's voucher; its matching <c>receipt_voucher</c> ledger entry arrives via
/// <c>/sync/ledger_entries</c>, so this handler does not post to the ledger itself. PATCH/DELETE are
/// rejected. Idempotent per environment.
/// </summary>
public sealed class ReceiptVouchersSyncHandler : ISyncTableHandler
{
    public const string TableName = "receipt_vouchers";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IClock _clock;

    public ReceiptVouchersSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        var envId = RequireEnvironment();

        var idempotencyKey = SyncBody.RequireString(body, "idempotency_key");
        var replay = await _db.ReceiptVouchers.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.EnvironmentId == envId && v.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return new SyncWriteResult(replay.Id, replay.UpdatedAt);
        }

        if (await _db.ReceiptVouchers.IgnoreQueryFilters().AnyAsync(v => v.Id == id, cancellationToken))
        {
            throw new ConflictException("receipt_voucher_already_exists",
                $"Receipt voucher '{id}' already exists; receipt vouchers are append-only.");
        }

        var customerId = SyncBody.RequireGuid(body, "customer_id");
        await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken), "customer", customerId);

        var issuedBy = SyncBody.RequireGuid(body, "issued_by");
        await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == issuedBy, cancellationToken), "user", issuedBy);

        var amount = SyncBody.RequireDecimal(body, "amount");
        if (amount <= 0m)
        {
            throw new ConflictException("invalid_voucher_amount", "voucher amount must be greater than zero.");
        }

        var voucher = new ReceiptVoucher
        {
            Id = id,
            CustomerId = customerId,
            Amount = amount,
            Method = SyncBody.RequireString(body, "method", PaymentMethod.All, TableName),
            IssuedBy = issuedBy,
            IssuedAt = SyncBody.OptionalDateTime(body, "issued_at") ?? _clock.UtcNow,
            Notes = SyncBody.OptionalString(body, "notes"),
            IdempotencyKey = idempotencyKey,
        };

        _db.ReceiptVouchers.Add(voucher);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(voucher.Id, voucher.UpdatedAt);
    }

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw AppendOnly();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw AppendOnly();

    private static ConflictException AppendOnly() => new(
        "receipt_vouchers_append_only", "receipt vouchers are append-only and server-authoritative.");

    private Guid RequireEnvironment()
    {
        if (_user.EnvironmentId is not { } envId || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return envId;
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
