using System.Text.Json;
using VetSystem.API.Filters;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// Generic <c>/sync/{table}</c> route group — the only write path used by PowerSync's upload
/// connector and the web offline outbox. Auth → per-user rate limit (the <c>"sync"</c> token-bucket
/// policy, M13 task 10) → idempotency filters bind once here; each milestone adds a
/// <see cref="ISyncTableHandler"/> for its tables to plug into this pipeline.
/// </summary>
public sealed class SyncModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/sync/{table}")
            .RequireAuthorization()
            .RequireRateLimiting("sync")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .WithTags("Sync");

        group.MapPut("/", PutAsync)
            .WithName("Sync_Put")
            .WithSummary("Insert a row (Guid v7 supplied by client).");

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithName("Sync_Patch")
            .WithSummary("Partial update — last-write-wins for non-financial fields.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("Sync_Delete")
            .WithSummary("Soft delete (sets deleted_at).");
    }

    private static async Task<IResult> PutAsync(
        string table,
        JsonElement body,
        ISyncDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetProperty("id", out var idElement) || !idElement.TryGetGuid(out var id))
        {
            throw new ConflictException("missing_client_id",
                "Sync inserts require an 'id' (Guid v7) generated client-side.");
        }

        var handler = dispatcher.Resolve(table);
        var result = await handler.PutAsync(id, body, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.Id));
    }

    private static async Task<IResult> PatchAsync(
        string table,
        Guid id,
        JsonElement body,
        ISyncDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var handler = dispatcher.Resolve(table);
        var result = await handler.PatchAsync(id, body, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.Id));
    }

    private static async Task<IResult> DeleteAsync(
        string table,
        Guid id,
        ISyncDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var handler = dispatcher.Resolve(table);
        var result = await handler.DeleteAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(result.Id));
    }
}
