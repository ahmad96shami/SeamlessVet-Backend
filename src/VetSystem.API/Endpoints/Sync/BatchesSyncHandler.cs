using System.Text.Json;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/batches</c> (M8 task 12) is server-authoritative and rejects every client write. Batch
/// financial configuration — supervision fee model, the per-batch entitlement override, the doctor
/// share — is an Admin/Accountant, online operation (PRD §7, §8.9): money-affecting decisions are
/// server-confirmed, never authored offline. The device pulls batches read-only via the
/// <c>doctor_scope</c> sync rules and configures them through the online <c>/batches</c> endpoints.
/// Mirrors the read-only rejection of <see cref="StockItemsSyncHandler"/>.
/// </summary>
public sealed class BatchesSyncHandler : ISyncTableHandler
{
    public const string TableName = "batches";

    public string Table => TableName;

    public Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw Reject();

    private static MethodNotAllowedException Reject() => new(
        "batches_server_authoritative",
        "batches are server-authoritative financial configuration and cannot be written from a client. "
        + "Create or edit them online via the /batches endpoints (Admin/Accountant); the device receives them read-only.");
}
