using System.Text.Json;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/stock_items</c> rejects every client write with 405 (M4 task 11). Stock balances are a
/// server-derived materialized count — clients change stock only by posting signed deltas to
/// <c>/sync/inventory_movements</c>; writing an absolute quantity here is forbidden
/// (SCHEMA "Key invariants" #2). Mirrors the M3 <see cref="LedgersSyncHandler"/> server-managed
/// rejection, but as 405 since the method itself is not permitted on this resource.
/// </summary>
public sealed class StockItemsSyncHandler : ISyncTableHandler
{
    public const string TableName = "stock_items";

    public string Table => TableName;

    public Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw Reject();

    private static MethodNotAllowedException Reject() => new(
        "stock_items_server_managed",
        "stock_items are a server-derived balance and cannot be written directly. Post signed deltas "
        + "to /sync/inventory_movements; the server recomputes stock_items.quantity.");
}
