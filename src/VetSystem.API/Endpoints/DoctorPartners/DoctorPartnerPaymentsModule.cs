using VetSystem.API.DoctorPartners;
using VetSystem.API.Filters;
using VetSystem.Application.DoctorPartners.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.DoctorPartners;

/// <summary>
/// M30 doctor-partner payments (SCHEMA §4) — <c>/doctor-partners/{id}/payments</c>. Posting one reduces
/// the doctor's balance. Writes require <see cref="PermissionKey.DoctorPartnersPay"/>.
/// </summary>
public sealed class DoctorPartnerPaymentsModule : IEndpointModule
{
    private const string EntityType = "doctor_partner_payment";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/doctor-partners/{doctorPartnerId:guid}/payments")
            .RequireAuthorization()
            .WithTags("Doctor Partners");

        group.MapGet("/", List)
            .WithName("DoctorPartnerPayments_List");

        group.MapPost("/", Create)
            .RequirePermission(PermissionKey.DoctorPartnersPay)
            .AddEndpointFilter<ValidationFilter<DoctorPartnerPaymentRequest>>()
            .AddEndpointFilter(new IdempotencyKeyFilter(EntityType))
            .WithName("DoctorPartnerPayments_Create");
    }

    private static async Task<IResult> List(
        Guid doctorPartnerId, DoctorPartnerPaymentsService svc, int? skip, int? take, CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(doctorPartnerId, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    private static async Task<IResult> Create(
        Guid doctorPartnerId,
        DoctorPartnerPaymentRequest request,
        DoctorPartnerPaymentsService svc,
        CancellationToken cancellationToken)
    {
        var item = await svc.IssueAsync(doctorPartnerId, request, cancellationToken);
        return TypedResults.Ok(new IdentifierResponse(item.Id));
    }
}
