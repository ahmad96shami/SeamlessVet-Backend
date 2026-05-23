using VetSystem.API.Appointments;
using VetSystem.API.Filters;
using VetSystem.Application.Appointments.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Appointments;

/// <summary>
/// Appointment scheduling endpoints (PRD §5.3, M6 tasks 2, 6, 7). Reads need only authentication;
/// writes require <see cref="PermissionKey.AppointmentsWrite"/> and an idempotency key. Terminal
/// transitions are dedicated actions (<c>/attend</c>, <c>/cancel</c>, <c>/no-show</c>) so each is an
/// auditable single step, never a side effect of a PATCH.
/// </summary>
public sealed class AppointmentsModule : IEndpointModule
{
    private const string EntityType = "appointment";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/appointments")
            .RequireAuthorization()
            .WithTags("Appointments");

        group.MapGet("/", List).WithName("Appointments_List");
        group.MapGet("/{id:guid}", Get).WithName("Appointments_Get");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.AppointmentsWrite)
            .AddEndpointFilter<ValidationFilter<AppointmentCreateRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Appointments_Create");

        group.MapPatch("/{id:guid}", Update)
            .RequirePermission(PermissionKey.AppointmentsWrite)
            .AddEndpointFilter<ValidationFilter<AppointmentPatchRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("Appointments_Update");

        group.MapPost("/{id:guid}/attend", Attend)
            .RequirePermission(PermissionKey.AppointmentsWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter("appointment_attend"))
            .WithName("Appointments_Attend");

        group.MapPost("/{id:guid}/cancel", Cancel)
            .RequirePermission(PermissionKey.AppointmentsWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter("appointment_cancel"))
            .WithName("Appointments_Cancel");

        group.MapPost("/{id:guid}/no-show", NoShow)
            .RequirePermission(PermissionKey.AppointmentsWrite)
            .AddEndpointFilter(new IdempotencyKeyFilter("appointment_no_show"))
            .WithName("Appointments_NoShow");
    }

    private static async Task<IResult> List(
        AppointmentsService svc,
        Guid? doctorId,
        Guid? customerId,
        Guid? petId,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(doctorId, customerId, petId, status, from, to, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Get(Guid id, AppointmentsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.GetAsync(id, cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<IResult> Create(
        AppointmentCreateRequest request,
        AppointmentsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.CreateAsync(request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Update(
        Guid id,
        AppointmentPatchRequest request,
        AppointmentsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.UpdateAsync(id, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Attend(Guid id, AppointmentsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.AttendAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> Cancel(Guid id, AppointmentsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.CancelAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }

    private static async Task<IResult> NoShow(Guid id, AppointmentsService svc, CancellationToken cancellationToken)
    {
        var item = await svc.NoShowAsync(id, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
