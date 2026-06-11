using VetSystem.API.Doctors;

namespace VetSystem.API.Endpoints.Doctors;

/// <summary>
/// GET /doctors — the environment's veterinarian roster for the visit / appointment / follow-up
/// doctor pickers. Authenticated-only: every staff role that opens a visit needs to assign a doctor,
/// but none of them hold <c>users.manage</c>, so the admin users endpoint is not an option.
/// </summary>
public sealed class DoctorsModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/doctors")
            .RequireAuthorization()
            .WithTags("Doctors");

        group.MapGet("/", List).WithName("Doctors_List");
    }

    private static async Task<IResult> List(DoctorsReadService svc, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(cancellationToken);
        return TypedResults.Ok(items);
    }
}
