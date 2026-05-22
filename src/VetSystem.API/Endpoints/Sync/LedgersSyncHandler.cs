using System.Text.Json;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/ledgers</c> rejects all client writes (M3). Ledger rows are derived state — they are
/// created by <see cref="CustomersSyncHandler"/> alongside the parent customer, mutated only via
/// <see cref="VetSystem.Application.Ledgers.ILedgerService"/> when entries post, and closed by the
/// M9 account-close endpoint. Same shape as the inventory <c>stock_items</c> rejection in M4.
/// </summary>
public sealed class LedgersSyncHandler : ISyncTableHandler
{
    public const string TableName = "ledgers";

    public string Table => TableName;

    public Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw Reject();

    private static ConflictException Reject() => new(
        "ledgers_server_managed",
        "ledgers are derived state. Create them via /sync/customers (auto-created with the customer), "
        + "post entries via /sync/ledger_entries, and close accounts via the M9 close-account endpoint.");
}
