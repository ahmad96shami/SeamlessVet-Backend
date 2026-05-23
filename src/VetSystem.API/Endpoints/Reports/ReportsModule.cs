using VetSystem.API.Entitlements;
using VetSystem.API.Filters;
using VetSystem.API.Reports;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Reports;
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
        group.MapGet("/profit-per-batch", ProfitPerBatch).WithName("Reports_ProfitPerBatch");
        group.MapGet("/farm-account-status", FarmAccountStatus).WithName("Reports_FarmAccountStatus");
        group.MapGet("/doctor-entitlements", DoctorEntitlements).WithName("Reports_DoctorEntitlements");
        group.MapGet("/upcoming-vaccinations", UpcomingVaccinations).WithName("Reports_UpcomingVaccinations");
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

    /// <summary>GET /reports/profit-per-batch — revenue/cost, drug profit, doctor &amp; clinic share, partner split.</summary>
    private static async Task<IResult> ProfitPerBatch(
        ProfitPerBatchReportService svc,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(batchId, cancellationToken);
        return TypedResults.Ok(report);
    }

    /// <summary>GET /reports/farm-account-status — the customer's full ledger statement (reuses M3).</summary>
    private static async Task<IResult> FarmAccountStatus(
        ILedgerService ledgers,
        Guid customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (fromInstant, toInstant) = ReportQuery.ResolveStatementBounds(from, to);
        var statement = await ledgers.GetStatementAsync(customerId, fromInstant, toInstant, cancellationToken);
        return TypedResults.Ok(statement);
    }

    /// <summary>GET /reports/doctor-entitlements — entitlements by doctor + status (reuses the M9 list).</summary>
    private static async Task<IResult> DoctorEntitlements(
        EntitlementSettlementService svc,
        Guid? doctorId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(doctorId, status, skip, take, cancellationToken);
        return TypedResults.Ok(items);
    }

    /// <summary>GET /reports/upcoming-vaccinations — vaccinations due in a date range, by customer.</summary>
    private static async Task<IResult> UpcomingVaccinations(
        UpcomingVaccinationsReportService svc,
        DateOnly? from,
        DateOnly? to,
        Guid? customerId,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, customerId, skip, take, cancellationToken);
        return TypedResults.Ok(report);
    }
}
