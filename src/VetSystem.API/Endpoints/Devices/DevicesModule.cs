using VetSystem.API.Devices;
using VetSystem.API.Filters;
using VetSystem.Application.Devices.Contracts;

namespace VetSystem.API.Endpoints.Devices;

/// <summary>
/// M21 — push-token registration for the caller's own device. Self-scoped like
/// <c>/notifications</c>, so authentication is the only gate (no permission key); both writes are
/// naturally idempotent (upsert / delete-if-mine), so no <c>IdempotencyKeyFilter</c> either.
/// Unregister is a POST (not DELETE) because the token travels in the body and DELETE bodies
/// model poorly through NSwag → openapi-typescript.
/// </summary>
public sealed class DevicesModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/devices")
            .RequireAuthorization()
            .WithTags("Devices");

        group.MapPost("/push-token", Register)
            .AddEndpointFilter<ValidationFilter<RegisterPushTokenRequest>>()
            .WithName("Devices_RegisterPushToken");

        group.MapPost("/push-token/unregister", Unregister)
            .AddEndpointFilter<ValidationFilter<UnregisterPushTokenRequest>>()
            .WithName("Devices_UnregisterPushToken");
    }

    private static async Task<IResult> Register(
        RegisterPushTokenRequest request, DeviceTokensService svc, CancellationToken cancellationToken)
    {
        await svc.RegisterAsync(request, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Unregister(
        UnregisterPushTokenRequest request, DeviceTokensService svc, CancellationToken cancellationToken)
    {
        await svc.UnregisterAsync(request, cancellationToken);
        return TypedResults.NoContent();
    }
}
