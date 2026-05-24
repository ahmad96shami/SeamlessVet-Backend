using Hangfire;
using Microsoft.EntityFrameworkCore;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Health;

/// <summary>
/// <c>/health/live</c> = process responsive. <c>/health/ready</c> = deep readiness (M13 task 15):
/// the database is reachable (fatal — 503 if not), plus the Hangfire worker and the PowerSync logical
/// replication slot are reported as non-fatal sub-checks. A non-fatal check being down yields
/// <c>status: "degraded"</c> but still 200, since the API can serve HTTP while jobs/replication catch up;
/// only an unreachable database fails readiness.
/// </summary>
public sealed class HealthModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/health").WithTags("Health").AllowAnonymous();

        group.MapGet("/live", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("Health_Live")
            .WithSummary("Process liveness probe.");

        group.MapGet("/ready", ReadyAsync)
            .WithName("Health_Ready")
            .WithSummary("Readiness probe — database (fatal) + Hangfire + PowerSync replication slot.");
    }

    private static async Task<IResult> ReadyAsync(HttpContext httpContext, ApplicationDbContext db, CancellationToken ct)
    {
        var database = await db.Database.CanConnectAsync(ct) ? "ok" : "unreachable";
        if (database != "ok")
        {
            return Results.Json(
                new { status = "unhealthy", checks = new { database } },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var hangfire = CheckHangfire(httpContext.RequestServices);
        var powersync = await CheckPowerSyncSlotAsync(db, ct);

        var healthy = powersync == "ok" && hangfire is "ok" or "disabled";
        return Results.Ok(new
        {
            status = healthy ? "ok" : "degraded",
            checks = new { database, hangfire, powersync },
        });
    }

    /// <summary>Hangfire storage is registered in DI only when enabled (off in Test) — absent ⇒ "disabled".</summary>
    private static string CheckHangfire(IServiceProvider services)
    {
        var storage = services.GetService<JobStorage>();
        if (storage is null)
        {
            return "disabled";
        }

        try
        {
            return storage.GetMonitoringApi().Servers().Count > 0 ? "ok" : "no_active_server";
        }
        catch (Exception)
        {
            return "error";
        }
    }

    /// <summary>
    /// PowerSync replicates via a <b>logical</b> replication slot (the only logical replication on this
    /// DB); its presence means replication is wired. "missing" in dev/test where no PowerSync service runs.
    /// </summary>
    private static async Task<string> CheckPowerSyncSlotAsync(ApplicationDbContext db, CancellationToken ct)
    {
        try
        {
            var present = await db.Database
                .SqlQueryRaw<bool>(
                    "SELECT EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_type = 'logical') AS \"Value\"")
                .SingleAsync(ct);
            return present ? "ok" : "missing";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return "error";
        }
    }
}
