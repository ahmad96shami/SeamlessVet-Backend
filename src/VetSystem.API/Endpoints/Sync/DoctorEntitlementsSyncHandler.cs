using System.Text.Json;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/doctor_entitlements</c> (M9 task 13) is server-authoritative and rejects every client
/// write. Entitlements are computed, approved, and paid server-side through the settlement workflow
/// and the <c>/doctor-entitlements</c> endpoints — money the doctor is owed is never authored on a
/// device. The device pulls them read-only via the <c>doctor_scope</c> sync rules. Mirrors the
/// read-only rejection of <see cref="BatchesSyncHandler"/> and <see cref="StockItemsSyncHandler"/>.
/// </summary>
public sealed class DoctorEntitlementsSyncHandler : ISyncTableHandler
{
    public const string TableName = "doctor_entitlements";

    public string Table => TableName;

    public Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw Reject();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw Reject();

    private static MethodNotAllowedException Reject() => new(
        "doctor_entitlements_server_authoritative",
        "doctor_entitlements are server-authoritative and cannot be written from a client. They are "
        + "computed by the settlement workflow and approved/paid via the /doctor-entitlements endpoints; "
        + "the device receives them read-only.");
}
