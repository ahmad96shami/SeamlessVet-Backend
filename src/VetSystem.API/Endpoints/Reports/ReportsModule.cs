using VetSystem.API.Filters;
using VetSystem.API.Reports;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Reports;

/// <summary>
/// M12 — operational + financial reports (PRD §7.9). Every report is a read-only, environment-scoped,
/// offset-paged admin table gated at the group level by <see cref="PermissionKey.ReportsRead"/>
/// (Admin + Accountant by default). Endpoints are added here as each report lands; Excel/PDF export
/// (<c>?format=xlsx|pdf</c>) arrives in M12 tasks 12–13.
/// </summary>
public sealed class ReportsModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/reports")
            .RequireAuthorization()
            .WithTags("Reports");

        group.RequirePermission(PermissionKey.ReportsRead);

        group.MapGet("/doctor-income", DoctorIncome).WithName("Reports_DoctorIncome");
        group.MapGet("/clinic-profits", ClinicProfits).WithName("Reports_ClinicProfits");
    }

    /// <summary>GET /reports/doctor-income — visit count, revenue and calculated share per doctor.</summary>
    private static async Task<IResult> DoctorIncome(
        DoctorIncomeReportService svc,
        DateOnly? from,
        DateOnly? to,
        Guid? doctorId,
        string? visitType,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, doctorId, visitType, skip, take, cancellationToken);
        return TypedResults.Ok(report);
    }

    /// <summary>GET /reports/clinic-profits — revenue, COGS, gross-margin net profit and the partner split.</summary>
    private static async Task<IResult> ClinicProfits(
        ClinicProfitsReportService svc,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, cancellationToken);
        return TypedResults.Ok(report);
    }
}
