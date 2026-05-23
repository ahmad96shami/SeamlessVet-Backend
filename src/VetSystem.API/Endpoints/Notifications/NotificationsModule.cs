using VetSystem.API.Notifications;

namespace VetSystem.API.Endpoints.Notifications;

/// <summary>
/// The in-app notification feed for the authenticated user (M11 task 8). Both endpoints are
/// self-scoped — a user only ever sees and mutates their own notifications — so authentication is the
/// only gate (no permission key). Realtime push happens over <c>NotificationsHub</c>; this is the
/// durable feed the client lists and marks read.
/// </summary>
public sealed class NotificationsModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/notifications")
            .RequireAuthorization()
            .WithTags("Notifications");

        group.MapGet("/", List).WithName("Notifications_List");
        group.MapPost("/{id:guid}/read", MarkRead).WithName("Notifications_MarkRead");
    }

    private static async Task<IResult> List(
        NotificationsService svc, bool? unreadOnly, int? skip, int? take, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(unreadOnly ?? false, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> MarkRead(Guid id, NotificationsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.MarkReadAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }
}
