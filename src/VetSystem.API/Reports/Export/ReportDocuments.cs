using System.Globalization;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Application.Partnership;
using VetSystem.Application.Reports.Contracts;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// Projects each report's typed JSON DTO into the export-neutral <see cref="ReportDocument"/> (M12
/// tasks 12–13). One method per report endpoint; the Excel and PDF writers consume the result, so this
/// is the single place that decides a report's Arabic title, which filters/KPIs to surface, and the
/// shape of its table(s). <see cref="FileBaseName"/> stays ASCII (kebab-case) so the
/// <c>Content-Disposition</c> filename is header-safe; the human-facing title inside the file is Arabic.
/// </summary>
public static class ReportDocuments
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ----- M12 task 2: doctor income ------------------------------------------------------------
    public static ReportDocument DoctorIncome(DoctorIncomeReportResponse r) => new(
        Title: ReportLabels.DoctorIncomeTitle,
        FileBaseName: "doctor-income",
        Filters: Fields(
            DayField(ReportLabels.From, r.From),
            DayField(ReportLabels.To, r.To),
            Opt(ReportLabels.VisitTypeLabel, r.VisitType is null ? null : ReportLabels.VisitType(r.VisitType))),
        Summary: Fields(
            IntField(ReportLabels.DoctorCount, r.DoctorCount),
            IntField(ReportLabels.VisitCount, r.TotalVisitCount),
            MoneyField(ReportLabels.TotalRevenue, r.TotalRevenue),
            MoneyField(ReportLabels.TotalShare, r.TotalCalculatedShare)),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.Doctor, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.VisitCount, ReportCellKind.Integer),
                    new ReportColumn(ReportLabels.Revenue, ReportCellKind.Money),
                    new ReportColumn(ReportLabels.DoctorShare, ReportCellKind.Money),
                ],
                Rows: r.Rows.Select(row => Row(
                    ReportCell.Text(row.DoctorName),
                    ReportCell.Integer(row.VisitCount),
                    ReportCell.Money(row.TotalRevenue),
                    ReportCell.Money(row.CalculatedShare))).ToList()),
        ]);

    // ----- M12 task 3: clinic profits -----------------------------------------------------------
    public static ReportDocument ClinicProfits(ClinicProfitsReportResponse r) => new(
        Title: ReportLabels.ClinicProfitsTitle,
        FileBaseName: "clinic-profits",
        Filters: Fields(
            DayField(ReportLabels.From, r.From),
            DayField(ReportLabels.To, r.To),
            new ReportField(ReportLabels.AsOf, r.AsOf.ToString("yyyy-MM-dd", Inv))),
        Summary: Fields(
            MoneyField(ReportLabels.Revenue, r.Revenue),
            MoneyField(ReportLabels.Cogs, r.Cogs),
            MoneyField(ReportLabels.NetProfit, r.NetProfit),
            MoneyField(ReportLabels.DoctorShares, r.DoctorShares),
            MoneyField(ReportLabels.DistributedToPartners, r.DistributedToPartners),
            MoneyField(ReportLabels.RetainedByClinic, r.RetainedByClinic)),
        Tables: [PartnerAllocationsTable(r.PartnerAllocations)]);

    // ----- M12 task 4: profit per farm batch ----------------------------------------------------
    public static ReportDocument ProfitPerBatch(ProfitPerBatchReportResponse r) => new(
        Title: ReportLabels.ProfitPerBatchTitle,
        FileBaseName: "profit-per-batch",
        Filters: Fields(
            new ReportField(ReportLabels.Batch, r.BatchId.ToString()),
            new ReportField(ReportLabels.Customer, r.CustomerId.ToString()),
            new ReportField(ReportLabels.Doctor, r.DoctorId.ToString()),
            new ReportField(ReportLabels.AsOf, r.AsOf.ToString("yyyy-MM-dd", Inv))),
        Summary: Fields(
            new ReportField(ReportLabels.System, ReportLabels.EntitlementSystem(r.EntitlementSystem)),
            new ReportField(ReportLabels.EntitlementEnabled, ReportLabels.Bool(r.EntitlementEnabled)),
            MoneyField(ReportLabels.Revenue, r.Revenue),
            MoneyField(ReportLabels.DrugCost, r.DrugCost),
            MoneyField(ReportLabels.DrugProfit, r.DrugProfit),
            MoneyField(ReportLabels.ExamFee, r.ExamFee),
            MoneyField(ReportLabels.DoctorShare, r.DoctorShare),
            r.CeilingApplied is { } ceil ? MoneyField(ReportLabels.CeilingApplied, ceil) : null,
            MoneyField(ReportLabels.ClinicShare, r.ClinicShare),
            MoneyField(ReportLabels.DistributedToPartners, r.DistributedToPartners),
            MoneyField(ReportLabels.RetainedByClinic, r.RetainedByClinic)),
        Tables: [PartnerAllocationsTable(r.PartnerAllocations)]);

    // ----- M12 task 5: farm account status (ledger statement, reuses M3) ------------------------
    public static ReportDocument FarmAccountStatus(StatementResponse r) => new(
        Title: ReportLabels.FarmAccountStatusTitle,
        FileBaseName: "farm-account-status",
        Filters: Fields(
            new ReportField(ReportLabels.Customer, r.CustomerName),
            DayField(ReportLabels.From, AsDay(r.From)),
            DayField(ReportLabels.To, AsDay(r.To))),
        Summary: Fields(
            MoneyField(ReportLabels.OpeningBalance, r.OpeningBalance),
            MoneyField(ReportLabels.ClosingBalance, r.ClosingBalance),
            new ReportField(ReportLabels.Status, ReportLabels.LedgerStatus(r.Status))),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.Date, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.EntryType, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Description, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Amount, ReportCellKind.Money),
                    new ReportColumn(ReportLabels.Balance, ReportCellKind.Money),
                ],
                Rows: r.Entries.Select(e => Row(
                    ReportCell.Moment(e.CreatedAt),
                    ReportCell.Text(ReportLabels.LedgerEntryType(e.EntryType)),
                    ReportCell.Text(e.Description),
                    ReportCell.Money(e.Amount),
                    ReportCell.Money(e.BalanceAfter))).ToList()),
        ]);

    // ----- M12 task 6: doctor entitlements (reuses M9 list) -------------------------------------
    public static ReportDocument DoctorEntitlements(IReadOnlyList<DoctorEntitlementResponse> rows) => new(
        Title: ReportLabels.DoctorEntitlementsTitle,
        FileBaseName: "doctor-entitlements",
        Filters: [],
        Summary: Fields(
            IntField(ReportLabels.Count, rows.Count),
            MoneyField(ReportLabels.Total, rows.Sum(e => e.ComputedAmount))),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.Doctor, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.System, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Amount, ReportCellKind.Money),
                    new ReportColumn(ReportLabels.CeilingApplied, ReportCellKind.Money),
                    new ReportColumn(ReportLabels.Status, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.ApprovedAt, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.PaidAt, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.PaidMethod, ReportCellKind.Text),
                ],
                Rows: rows.Select(e => Row(
                    ReportCell.Id(e.DoctorId),
                    ReportCell.Text(ReportLabels.EntitlementSystem(e.CalculationSystem)),
                    ReportCell.Money(e.ComputedAmount),
                    ReportCell.MoneyOrDash(e.CeilingApplied),
                    ReportCell.Text(ReportLabels.EntitlementStatus(e.Status)),
                    ReportCell.Moment(e.ApprovedAt),
                    ReportCell.Moment(e.PaidAt),
                    ReportCell.Text(e.PaidMethod is null ? "—" : ReportLabels.PaymentMethod(e.PaidMethod)))).ToList()),
        ]);

    // ----- M12 task 7: sales --------------------------------------------------------------------
    public static ReportDocument Sales(SalesReportResponse r) => new(
        Title: ReportLabels.SalesTitle,
        FileBaseName: "sales",
        Filters: Fields(
            DayField(ReportLabels.From, r.From),
            DayField(ReportLabels.To, r.To),
            IdField(ReportLabels.Cashier, r.CashierId)),
        Summary: Fields(
            MoneyField(ReportLabels.Total, r.Total),
            IntField(ReportLabels.InvoiceCount, r.InvoiceCount)),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.PaymentMethodHeader, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Amount, ReportCellKind.Money),
                    new ReportColumn(ReportLabels.PaymentCount, ReportCellKind.Integer),
                ],
                Rows: r.ByMethod.Select(m => Row(
                    ReportCell.Text(ReportLabels.PaymentMethod(m.Method)),
                    ReportCell.Money(m.Amount),
                    ReportCell.Integer(m.PaymentCount))).ToList()),
        ]);

    // ----- M12 task 8: profit and loss ----------------------------------------------------------
    public static ReportDocument ProfitAndLoss(ProfitAndLossResponse r) => new(
        Title: ReportLabels.ProfitAndLossTitle,
        FileBaseName: "profit-and-loss",
        Filters: Fields(DayField(ReportLabels.From, r.From), DayField(ReportLabels.To, r.To)),
        Summary: Fields(
            MoneyField(ReportLabels.Revenue, r.Revenue),
            MoneyField(ReportLabels.TaxCollected, r.TaxCollected),
            MoneyField(ReportLabels.Cogs, r.Cogs),
            MoneyField(ReportLabels.GrossProfit, r.GrossProfit),
            MoneyField(ReportLabels.DoctorShares, r.DoctorShares)),
        Tables: []);

    // ----- M12 task 9: inventory movement -------------------------------------------------------
    public static ReportDocument InventoryMovement(InventoryMovementReportResponse r) => new(
        Title: ReportLabels.InventoryMovementTitle,
        FileBaseName: "inventory-movement",
        Filters: Fields(
            DayField(ReportLabels.From, r.From),
            DayField(ReportLabels.To, r.To),
            IdField(ReportLabels.Product, r.ProductId),
            Opt(ReportLabels.LocationKind, r.LocationType is null ? null : ReportLabels.LocationType(r.LocationType)),
            IdField(ReportLabels.Location, r.LocationId)),
        Summary: Fields(IntField(ReportLabels.TotalCount, r.TotalCount)),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.LocationKind, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Location, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Product, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Inflows, ReportCellKind.Number),
                    new ReportColumn(ReportLabels.Outflows, ReportCellKind.Number),
                    new ReportColumn(ReportLabels.NetChange, ReportCellKind.Number),
                    new ReportColumn(ReportLabels.Balance, ReportCellKind.Number),
                ],
                Rows: r.Rows.Select(row => Row(
                    ReportCell.Text(ReportLabels.LocationType(row.LocationType)),
                    ReportCell.Id(row.LocationId),
                    ReportCell.Id(row.ProductId),
                    ReportCell.Number(row.Inflows),
                    ReportCell.Number(row.Outflows),
                    ReportCell.Number(row.NetChange),
                    ReportCell.Number(row.Balance))).ToList()),
        ]);

    // ----- M12 task 10: field-doctor visits -----------------------------------------------------
    public static ReportDocument FieldDoctorVisits(FieldDoctorVisitsReportResponse r) => new(
        Title: ReportLabels.FieldDoctorVisitsTitle,
        FileBaseName: "field-doctor-visits",
        Filters: Fields(
            DayField(ReportLabels.From, r.From),
            DayField(ReportLabels.To, r.To),
            IdField(ReportLabels.Doctor, r.DoctorId)),
        Summary: Fields(IntField(ReportLabels.TotalCount, r.TotalCount)),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.VisitNumber, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Customer, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Status, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.StartedAt, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.EndedAt, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Services, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Medications, ReportCellKind.Text),
                ],
                Rows: r.Rows.Select(v => Row(
                    ReportCell.Text(v.VisitNumber),
                    ReportCell.Id(v.CustomerId),
                    ReportCell.Text(ReportLabels.VisitStatus(v.Status)),
                    ReportCell.Moment(v.StartedAt),
                    ReportCell.Moment(v.EndedAt),
                    ReportCell.Text(JoinServices(v.Services)),
                    ReportCell.Text(JoinMedications(v.Medications)))).ToList()),
        ]);

    // ----- M12 task 14: KPI summary -------------------------------------------------------------
    public static ReportDocument KpiSummary(KpiSummaryResponse r) => new(
        Title: ReportLabels.KpiSummaryTitle,
        FileBaseName: "kpi-summary",
        Filters: Fields(new ReportField(ReportLabels.AsOf, r.AsOf.ToString("yyyy-MM-dd", Inv))),
        Summary: Fields(
            IntField(ReportLabels.VisitsToday, r.VisitsToday),
            MoneyField(ReportLabels.RevenueThisMonth, r.RevenueThisMonth),
            IntField(ReportLabels.PendingEntitlements, r.PendingEntitlements),
            IntField(ReportLabels.LowStockItems, r.LowStockItems)),
        Tables: []);

    // ----- M12 task 11: upcoming vaccinations ---------------------------------------------------
    public static ReportDocument UpcomingVaccinations(UpcomingVaccinationsReportResponse r) => new(
        Title: ReportLabels.UpcomingVaccinationsTitle,
        FileBaseName: "upcoming-vaccinations",
        Filters: Fields(
            DayField(ReportLabels.From, r.From),
            DayField(ReportLabels.To, r.To),
            IdField(ReportLabels.Customer, r.CustomerId)),
        Summary: Fields(IntField(ReportLabels.TotalCount, r.TotalCount)),
        Tables:
        [
            new ReportTable(
                Caption: null,
                Columns:
                [
                    new ReportColumn(ReportLabels.VaccineType, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.Customer, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.DateGiven, ReportCellKind.Text),
                    new ReportColumn(ReportLabels.NextDueDate, ReportCellKind.Text),
                ],
                Rows: r.Rows.Select(v => Row(
                    ReportCell.Text(v.VaccineType),
                    ReportCell.Id(v.CustomerId),
                    ReportCell.Day(v.DateGiven),
                    ReportCell.Day(v.NextDueDate))).ToList()),
        ]);

    // ----- shared helpers -----------------------------------------------------------------------

    private static ReportTable PartnerAllocationsTable(IReadOnlyList<ProfitAllocation> allocations) => new(
        Caption: ReportLabels.DistributedToPartners,
        Columns:
        [
            new ReportColumn(ReportLabels.Partner, ReportCellKind.Text),
            new ReportColumn(ReportLabels.SharePercent, ReportCellKind.Percent),
            new ReportColumn(ReportLabels.Amount, ReportCellKind.Money),
        ],
        Rows: allocations.Select(a => Row(
            ReportCell.Text(a.DisplayName),
            ReportCell.Percent(a.SharePercent),
            ReportCell.Money(a.Amount))).ToList());

    private static string JoinServices(IReadOnlyList<FieldVisitServiceLine> services) =>
        services.Count == 0
            ? "—"
            : string.Join("، ", services.Select(s => s.ServiceName ?? s.ServiceId?.ToString() ?? "—"));

    private static string JoinMedications(IReadOnlyList<FieldVisitMedicationLine> meds) =>
        meds.Count == 0
            ? "—"
            : string.Join("، ", meds.Select(m =>
            {
                var name = m.ProductName ?? m.ProductId.ToString();
                return m.Quantity is { } q ? $"{name} × {q.ToString("0.###", Inv)}" : name;
            }));

    private static IReadOnlyList<ReportCell> Row(params ReportCell[] cells) => cells;

    private static List<ReportField> Fields(params ReportField?[] fields) =>
        fields.Where(f => f is not null).Select(f => f!).ToList();

    private static ReportField? Opt(string label, string? value) =>
        string.IsNullOrEmpty(value) ? null : new ReportField(label, value);

    private static ReportField? DayField(string label, DateOnly? value) =>
        value is { } d ? new ReportField(label, d.ToString("yyyy-MM-dd", Inv)) : null;

    private static ReportField? IdField(string label, Guid? value) =>
        value is { } g ? new ReportField(label, g.ToString()) : null;

    private static ReportField MoneyField(string label, decimal value) =>
        new(label, value.ToString("#,##0.00", Inv));

    private static ReportField IntField(string label, int value) =>
        new(label, value.ToString("#,##0", Inv));

    private static DateOnly? AsDay(DateTimeOffset? value) =>
        value is { } v ? DateOnly.FromDateTime(v.UtcDateTime) : null;
}
