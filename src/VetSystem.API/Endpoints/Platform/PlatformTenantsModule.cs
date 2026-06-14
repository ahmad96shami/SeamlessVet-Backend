using VetSystem.API.Filters;
using VetSystem.API.Platform;
using VetSystem.Application.Platform.Contracts;
using VetSystem.Application.Provisioning;

namespace VetSystem.API.Endpoints.Platform;

/// <summary>
/// M35 — the minimal platform console: list / get / provision / suspend / reactivate centers. The
/// whole group requires a <c>platform_admin</c> token (<see cref="RequirePlatformAdminFilter"/>); a
/// tenant token gets <c>platform_admin_required</c> and the env-scoped filter blocks the reverse. No
/// idempotency-key filter (rare manual ops; the platform token is env-less, which the key filter
/// requires) and no rate-limit beyond the global defaults.
/// </summary>
public sealed class PlatformTenantsModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/platform/tenants")
            .WithTags("Platform")
            .RequireAuthorization()
            .RequirePlatformAdmin();

        group.MapGet("/", List)
            .WithName("Platform_Tenants_List")
            .WithSummary("List all centers with their user counts.");

        group.MapGet("/{id:guid}", Get)
            .WithName("Platform_Tenants_Get")
            .WithSummary("Get one center.");

        group.MapPost("/", Provision)
            .AddEndpointFilter<ValidationFilter<ProvisionEnvironmentRequest>>()
            .WithName("Platform_Tenants_Provision")
            .WithSummary("Provision a new center + its first admin (transactional create + seed).");

        group.MapPost("/{id:guid}/suspend", Suspend)
            .WithName("Platform_Tenants_Suspend")
            .WithSummary("Suspend a center (rejects its already-issued tokens within one request).");

        group.MapPost("/{id:guid}/reactivate", Reactivate)
            .WithName("Platform_Tenants_Reactivate")
            .WithSummary("Reactivate a suspended center.");
    }

    private static async Task<IResult> List(PlatformTenantsService tenants, CancellationToken cancellationToken)
    {
        var items = await tenants.ListAsync(cancellationToken);
        return TypedResults.Ok(new TenantListResponse(items));
    }

    private static async Task<IResult> Get(Guid id, PlatformTenantsService tenants, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(id, cancellationToken);
        return TypedResults.Ok(tenant);
    }

    private static async Task<IResult> Provision(
        ProvisionEnvironmentRequest request,
        PlatformTenantsService tenants,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.ProvisionAsync(request, cancellationToken);
        return TypedResults.Created($"/platform/tenants/{tenant.Id}", tenant);
    }

    private static async Task<IResult> Suspend(Guid id, PlatformTenantsService tenants, CancellationToken cancellationToken)
    {
        var tenant = await tenants.SuspendAsync(id, cancellationToken);
        return TypedResults.Ok(tenant);
    }

    private static async Task<IResult> Reactivate(Guid id, PlatformTenantsService tenants, CancellationToken cancellationToken)
    {
        var tenant = await tenants.ReactivateAsync(id, cancellationToken);
        return TypedResults.Ok(tenant);
    }
}
