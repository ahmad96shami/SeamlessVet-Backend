using VetSystem.API.Filters;
using VetSystem.API.Settings;
using VetSystem.Application.Settings.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Admin;

/// <summary>
/// Admin GET/PATCH for the per-environment <c>system_settings</c> singleton (PRD §5.7, M2).
/// PATCH is gated by <c>settings.write</c>, idempotency-keyed, and partial — only fields supplied
/// in the body are updated. Idempotency replay returns the cached <see cref="IdempotencyReplayResponse"/>
/// so a retried offline mutation cannot apply the same change twice.
/// </summary>
public sealed class SystemSettingsModule : IEndpointModule
{
    private const string EntityType = "system_settings";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/settings")
            .RequireAuthorization()
            .WithTags("Admin");

        group.MapGet("/", Get)
            .WithName("Admin_Settings_Get");

        group.MapPatch("/", Patch)
            .RequirePermission(PermissionKey.SettingsWrite)
            .AddEndpointFilter<ValidationFilter<SystemSettingsPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Admin_Settings_Patch");
    }

    private static async Task<IResult> Get(SystemSettingsAdminService svc, CancellationToken cancellationToken)
    {
        var snapshot = await svc.GetAsync(cancellationToken);
        return TypedResults.Ok(snapshot);
    }

    private static async Task<IResult> Patch(
        SystemSettingsPatchRequest request,
        SystemSettingsAdminService svc,
        CancellationToken cancellationToken)
    {
        var snapshot = await svc.PatchAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(snapshot.Id));
    }
}
