using System.Text.Json;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// Per-table write handler dispatched by <c>/sync/{table}</c>. Each domain milestone registers
/// one handler per table it owns. The route group enforces auth + idempotency centrally; the
/// handler enforces the table-specific business rules (settlement lock, delta-only inventory,
/// append-only ledger, etc. — SCHEMA "Key invariants").
/// </summary>
public interface ISyncTableHandler
{
    string Table { get; }

    Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken);

    Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken);

    Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record SyncWriteResult(Guid Id, DateTimeOffset UpdatedAt);
