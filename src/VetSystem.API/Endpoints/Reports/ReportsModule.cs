using VetSystem.API.Entitlements;
using VetSystem.API.Filters;
using VetSystem.API.Reports;
using VetSystem.API.Reports.Export;
using VetSystem.Application.Common;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Reports;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.API.Endpoints.Reports;

/// <summary>
/// M12 — operational + financial reports (PRD §7.9). The admin reports are gated at the group level by
/// <see cref="PermissionKey.ReportsRead"/> (Admin + Accountant by default; M12 task 15) and are offset-paged
/// admin tables, except the feed-like <c>field-doctor-visits</c> log which is cursor-paged (task 16). A
/// separate auth-only <c>/reports/my-income</c> lets any doctor see <b>their own</b> attributed income
/// without <see cref="PermissionKey.ReportsRead"/> (task 15, self-scoped to the caller). Each endpoint
/// takes an optional <c>?format=xlsx|pdf</c>: absent it returns the typed JSON DTO; otherwise
/// <see cref="ReportExporter"/> streams a generated Arabic/RTL Excel or PDF file (tasks 12–13).
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
        group.MapGet("/sales", Sales).WithName("Reports_Sales");
        group.MapGet("/profit-and-loss", ProfitAndLoss).WithName("Reports_ProfitAndLoss");
        group.MapGet("/inventory-movement", InventoryMovement).WithName("Reports_InventoryMovement");
        group.MapGet("/consumables", Consumables).WithName("Reports_Consumables");
        group.MapGet("/field-doctor-visits", FieldDoctorVisits).WithName("Reports_FieldDoctorVisits");
        group.MapGet("/kpi-summary", KpiSummary).WithName("Reports_KpiSummary");
        group.MapGet("/upcoming-vaccinations", UpcomingVaccinations).WithName("Reports_UpcomingVaccinations");
        group.MapGet("/pharmacy-profit", PharmacyProfit).WithName("Reports_PharmacyProfit");
        group.MapGet("/in-clinic-visit-profit", InClinicVisitProfit).WithName("Reports_InClinicVisitProfit");
        group.MapGet("/field-visit-profit", FieldVisitProfit).WithName("Reports_FieldVisitProfit");

        // M12 task 15 — doctor self-service. Auth-only (NOT gated on reports.read), self-scoped to the
        // caller: a field/clinic doctor sees only their own attributed income. Separate group so it
        // sits outside the admin reports.read gate above.
        var mine = endpoints.MapGroup("/reports")
            .RequireAuthorization()
            .WithTags("Reports");

        mine.MapGet("/my-income", MyIncome).WithName("Reports_MyIncome");
    }

    /// <summary>GET /reports/doctor-income — visit count, revenue and calculated share per doctor.</summary>
    private static async Task<IResult> DoctorIncome(
        DoctorIncomeReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        Guid? doctorId,
        string? visitType,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, doctorId, visitType, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.DoctorIncome);
    }

    /// <summary>
    /// GET /reports/my-income — the calling doctor's own income (M12 task 15). Auth-only and self-scoped:
    /// reuses the doctor-income report with the doctor fixed to the caller, so no <c>reports.read</c> is
    /// required and a doctor can never see another doctor's figures.
    /// </summary>
    private static async Task<IResult> MyIncome(
        DoctorIncomeReportService svc,
        ReportExporter export,
        ICurrentUserAccessor currentUser,
        DateOnly? from,
        DateOnly? to,
        string? visitType,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } doctorId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var report = await svc.BuildAsync(from, to, doctorId, visitType, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.DoctorIncome);
    }

    /// <summary>GET /reports/clinic-profits — revenue, COGS, gross-margin net profit and the partner split.</summary>
    private static async Task<IResult> ClinicProfits(
        ClinicProfitsReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.ClinicProfits);
    }

    /// <summary>GET /reports/profit-per-batch — revenue/cost, drug profit, doctor &amp; clinic share, partner split.</summary>
    private static async Task<IResult> ProfitPerBatch(
        ProfitPerBatchReportService svc,
        ReportExporter export,
        Guid batchId,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(batchId, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.ProfitPerBatch);
    }

    /// <summary>GET /reports/farm-account-status — the customer's full ledger statement (reuses M3).</summary>
    private static async Task<IResult> FarmAccountStatus(
        ILedgerService ledgers,
        ReportExporter export,
        Guid customerId,
        DateOnly? from,
        DateOnly? to,
        string? format,
        CancellationToken cancellationToken)
    {
        var (fromInstant, toInstant) = ReportQuery.ResolveStatementBounds(from, to);
        var statement = await ledgers.GetStatementAsync(customerId, fromInstant, toInstant, cancellationToken);
        return export.Resolve(format, statement, ReportDocuments.FarmAccountStatus);
    }

    /// <summary>GET /reports/doctor-entitlements — entitlements by doctor + status (reuses the M9 list).</summary>
    private static async Task<IResult> DoctorEntitlements(
        EntitlementSettlementService svc,
        ReportExporter export,
        Guid? doctorId,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var items = await svc.ListAsync(doctorId, skip, take, cancellationToken);
        return export.Resolve(format, items, ReportDocuments.DoctorEntitlements);
    }

    /// <summary>GET /reports/sales — money taken in over a period, broken down by payment method.</summary>
    private static async Task<IResult> Sales(
        SalesReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        Guid? cashierId,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, cashierId, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.Sales);
    }

    /// <summary>GET /reports/profit-and-loss — simplified income statement for a period.</summary>
    private static async Task<IResult> ProfitAndLoss(
        ProfitAndLossReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.ProfitAndLoss);
    }

    /// <summary>GET /reports/inventory-movement — inflows/outflows/balance per (location, product).</summary>
    private static async Task<IResult> InventoryMovement(
        InventoryMovementReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        Guid? productId,
        string? locationType,
        Guid? locationId,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, productId, locationType, locationId, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.InventoryMovement);
    }

    /// <summary>GET /reports/consumables — internal-use consumption (qty + FEFO cost) per (location, product) over a period (M27).</summary>
    private static async Task<IResult> Consumables(
        ConsumablesReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        Guid? productId,
        string? locationType,
        Guid? locationId,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, productId, locationType, locationId, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.Consumables);
    }

    /// <summary>GET /reports/field-doctor-visits — field visit log (cursor-paged via ?cursor/?limit), by doctor.</summary>
    private static async Task<IResult> FieldDoctorVisits(
        FieldDoctorVisitsReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        Guid? doctorId,
        string? cursor,
        int? limit,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, doctorId, cursor, limit, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.FieldDoctorVisits);
    }

    /// <summary>GET /reports/kpi-summary — dashboard snapshot (visits today, month revenue, pending entitlements, low stock).</summary>
    private static async Task<IResult> KpiSummary(
        KpiSummaryReportService svc,
        ReportExporter export,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(cancellationToken);
        return export.Resolve(format, report, ReportDocuments.KpiSummary);
    }

    /// <summary>GET /reports/upcoming-vaccinations — vaccinations due in a date range, by customer.</summary>
    private static async Task<IResult> UpcomingVaccinations(
        UpcomingVaccinationsReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        Guid? customerId,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, customerId, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.UpcomingVaccinations);
    }

    /// <summary>GET /reports/pharmacy-profit — drug/product revenue, cost and gross margin per product (M20).</summary>
    private static async Task<IResult> PharmacyProfit(
        PharmacyProfitReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.PharmacyProfit);
    }

    /// <summary>GET /reports/in-clinic-visit-profit — revenue/COGS/margin per in-clinic visit, farm/clinic-sliceable (M20).</summary>
    private static async Task<IResult> InClinicVisitProfit(
        InClinicVisitProfitReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        string? scope,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, scope, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.VisitProfit);
    }

    /// <summary>GET /reports/field-visit-profit — revenue/COGS/margin per field visit, farm/clinic-sliceable (M20).</summary>
    private static async Task<IResult> FieldVisitProfit(
        FieldVisitProfitReportService svc,
        ReportExporter export,
        DateOnly? from,
        DateOnly? to,
        string? scope,
        int? skip,
        int? take,
        string? format,
        CancellationToken cancellationToken)
    {
        var report = await svc.BuildAsync(from, to, scope, skip, take, cancellationToken);
        return export.Resolve(format, report, ReportDocuments.VisitProfit);
    }
}
