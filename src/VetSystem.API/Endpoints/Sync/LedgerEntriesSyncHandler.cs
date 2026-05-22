using System.Text.Json;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/ledger_entries</c> — append-only write path (M3 tasks 7, 10). PUT delegates to
/// <see cref="ILedgerService"/>; PATCH and DELETE throw a typed domain error
/// (SCHEMA "Key invariants" #3 — corrections are new <c>adjustment</c> rows, never updates).
/// </summary>
public sealed class LedgerEntriesSyncHandler : ISyncTableHandler
{
    public const string TableName = "ledger_entries";

    private readonly ILedgerService _ledgers;

    public LedgerEntriesSyncHandler(ILedgerService ledgers)
    {
        _ledgers = ledgers;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        var request = new LedgerEntryRequest(
            Id: id,
            LedgerId: SyncBody.RequireGuid(body, "ledger_id"),
            EntryType: SyncBody.RequireString(body, "entry_type"),
            Amount: SyncBody.RequireDecimal(body, "amount"),
            InvoiceId: SyncBody.OptionalGuid(body, "invoice_id"),
            ReceiptVoucherId: SyncBody.OptionalGuid(body, "receipt_voucher_id"),
            Description: SyncBody.OptionalString(body, "description"),
            IdempotencyKey: SyncBody.RequireString(body, "idempotency_key"));

        var entry = await _ledgers.AppendEntryAsync(request, cancellationToken);
        return new SyncWriteResult(entry.Id, entry.CreatedAt);
    }

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw AppendOnly();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw AppendOnly();

    private static ConflictException AppendOnly() => new(
        "ledger_entries_append_only",
        "ledger_entries are append-only. Post a new entry (entry_type='adjustment') to correct an error.");
}
