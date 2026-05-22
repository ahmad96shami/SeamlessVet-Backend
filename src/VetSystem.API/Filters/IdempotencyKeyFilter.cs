using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Filters;

/// <summary>
/// Reads the <c>Idempotency-Key</c> header and dedupes by <c>(environment_id, key)</c>.
/// On replay returns the original <c>result_ref</c> with HTTP 200 so retried offline
/// mutations (PowerSync upload-queue, web outbox) apply exactly once.
/// SCHEMA "Key invariants" — idempotency key required on every write.
/// </summary>
public sealed partial class IdempotencyKeyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    private readonly string? _entityType;

    /// <summary>
    /// Resolves <c>entity_type</c> for the idempotency record. Pass <c>null</c> (the default DI
    /// activation path) to read it from <c>{table}</c> route values — that is the <c>/sync/{table}</c>
    /// case. Pass a literal string for endpoints that don't have a table parameter in the route
    /// (e.g. <c>/admin/settings</c>).
    /// </summary>
    public IdempotencyKeyFilter(string? entityType = null)
    {
        _entityType = entityType;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            throw new ConflictException("idempotency_key_required",
                $"Header '{HeaderName}' is required on this endpoint.");
        }

        var key = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(key) || !ValidKey().IsMatch(key))
        {
            throw new ConflictException("idempotency_key_invalid",
                $"Header '{HeaderName}' must be 8–128 chars [A-Za-z0-9._-].");
        }

        var user = http.RequestServices.GetRequiredService<ICurrentUserAccessor>();
        if (user.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();

        var existing = await db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.EnvironmentId == envId && k.Key == key, http.RequestAborted);

        if (existing is not null)
        {
            return TypedResults.Ok(new IdempotencyReplayResponse(existing.ResultRef, Replay: true));
        }

        var entityType = _entityType ?? http.GetRouteValue("table")?.ToString();
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new InvalidOperationException(
                "IdempotencyKeyFilter could not resolve entity_type from route or constructor.");
        }

        // Reserve the key before running the handler so concurrent retries collapse to one apply.
        var reservation = new IdempotencyKey
        {
            Key = key,
            EnvironmentId = envId,
            EntityType = entityType,
            CreatedAt = http.RequestServices.GetRequiredService<IClock>().UtcNow,
        };
        db.IdempotencyKeys.Add(reservation);

        var reservationFailed = false;
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            reservationFailed = true;
        }

        if (reservationFailed)
        {
            var collided = await db.IdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    k => k.EnvironmentId == envId && k.Key == key,
                    http.RequestAborted);

            return TypedResults.Ok(new IdempotencyReplayResponse(collided?.ResultRef, Replay: true));
        }

        var result = await next(context);

        if (TryReadResultId(result) is { } resultId)
        {
            reservation.ResultRef = resultId;
            await db.SaveChangesAsync(http.RequestAborted);
        }

        return result;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";

    private static Guid? TryReadResultId(object? result) => result switch
    {
        Ok<IdentifierResponse> ok => ok.Value?.Id,
        IdentifierResponse id => id.Id,
        _ => null,
    };

    [GeneratedRegex("^[A-Za-z0-9._-]{8,128}$")]
    private static partial Regex ValidKey();
}

public sealed record IdempotencyReplayResponse(Guid? Id, bool Replay);

public sealed record IdentifierResponse(Guid Id);
