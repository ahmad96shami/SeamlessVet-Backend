using Microsoft.EntityFrameworkCore;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Health;

/// <summary>
/// <c>/health/live</c> = process responsive; <c>/health/ready</c> = also DB reachable.
/// Deep readiness checks (Hangfire alive, PowerSync replication-slot present) land in M13.
/// </summary>
public sealed class HealthModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/health").WithTags("Health").AllowAnonymous();

        group.MapGet("/live", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("Health_Live")
            .WithSummary("Process liveness probe.");

        group.MapGet("/ready", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var canConnect = await db.Database.CanConnectAsync(ct);
            return canConnect
                ? Results.Ok(new { status = "ok", checks = new { database = "ok" } })
                : Results.Json(
                    new { status = "degraded", checks = new { database = "unreachable" } },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .WithName("Health_Ready")
            .WithSummary("Process readiness probe — pings the database.");
    }
}
