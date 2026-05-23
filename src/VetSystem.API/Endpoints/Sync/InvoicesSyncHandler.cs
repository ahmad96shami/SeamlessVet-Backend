using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Financial;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/invoices</c> (M7 tasks 11, 13) — append-only, server-wins. PUT persists the device's
/// already-computed invoice header (totals snapshotted offline); the matching
/// <c>invoice_items</c> / <c>payments</c> and the ledger/inventory effects arrive through their own
/// sync tables, so this handler never re-posts the ledger or re-deducts stock. PATCH and DELETE are
/// rejected: a correction is a new invoice (a <c>void</c> row), never an in-place edit
/// (SCHEMA "Key invariants" #3, #5).
/// </summary>
public sealed class InvoicesSyncHandler : ISyncTableHandler
{
    public const string TableName = "invoices";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IClock _clock;
    private readonly IInvoiceNumberValidator _invoiceNumbers;

    public InvoicesSyncHandler(
        ApplicationDbContext db, ICurrentUserAccessor user, IClock clock, IInvoiceNumberValidator invoiceNumbers)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _invoiceNumbers = invoiceNumbers;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        var envId = RequireEnvironment();

        var idempotencyKey = SyncBody.RequireString(body, "idempotency_key");
        var replay = await _db.Invoices.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.EnvironmentId == envId && i.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return new SyncWriteResult(replay.Id, replay.UpdatedAt);
        }

        if (await _db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.Id == id, cancellationToken))
        {
            throw new ConflictException("invoice_already_exists", $"Invoice '{id}' already exists; invoices are append-only.");
        }

        var customerId = SyncBody.OptionalGuid(body, "customer_id");
        if (customerId is { } cid)
        {
            await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == cid, cancellationToken), "customer", cid);
        }

        var visitId = SyncBody.OptionalGuid(body, "visit_id");
        if (visitId is { } vid)
        {
            await EnsureExistsAsync(_db.Visits.AnyAsync(v => v.Id == vid, cancellationToken), "visit", vid);
        }

        var issuedBy = SyncBody.RequireGuid(body, "issued_by");
        await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == issuedBy, cancellationToken), "user", issuedBy);

        var number = SyncBody.OptionalString(body, "number");
        if (!string.IsNullOrWhiteSpace(number))
        {
            await _invoiceNumbers.ValidateAsync(number, excludeInvoiceId: null, cancellationToken);
        }
        else
        {
            number = null;
        }

        var status = SyncBody.OptionalString(body, "status") ?? InvoiceStatus.Issued;
        if (!InvoiceStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_invoice_status", $"status '{status}' is not valid.");
        }

        var subtotal = SyncBody.RequireDecimal(body, "subtotal");
        var discount = SyncBody.OptionalDecimal(body, "discount_amount") ?? 0m;
        var tax = SyncBody.OptionalDecimal(body, "tax_amount") ?? 0m;
        var total = SyncBody.RequireDecimal(body, "total");
        if (Round(subtotal - discount + tax) != Round(total))
        {
            throw new ConflictException("invoice_totals_mismatch",
                "total must equal subtotal - discount_amount + tax_amount.");
        }

        var invoice = new Invoice
        {
            Id = id,
            InvoiceType = SyncBody.RequireString(body, "invoice_type", InvoiceType.All, TableName),
            CustomerId = customerId,
            VisitId = visitId,
            BatchId = SyncBody.OptionalGuid(body, "batch_id"),
            Number = number,
            Subtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = tax,
            Total = total,
            Status = status,
            IssuedBy = issuedBy,
            IssuedAt = SyncBody.OptionalDateTime(body, "issued_at") ?? _clock.UtcNow,
            IdempotencyKey = idempotencyKey,
            VoidOfInvoiceId = SyncBody.OptionalGuid(body, "void_of_invoice_id"),
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(invoice.Id, invoice.UpdatedAt);
    }

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw AppendOnly();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw AppendOnly();

    private static ConflictException AppendOnly() => new(
        "invoices_append_only",
        "invoices are append-only and server-authoritative. Issue a void (status='void') to reverse one.");

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

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
