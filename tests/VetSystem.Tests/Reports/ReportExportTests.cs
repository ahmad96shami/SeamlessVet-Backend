using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using VetSystem.API.Reports.Export;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Application.Partnership;
using VetSystem.Application.Reports.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M12 tasks 12, 13 &amp; 19 — report export. Pure unit tests (no DB): each report DTO is projected by its
/// <see cref="ReportDocuments"/> mapper and rendered to XLSX (ClosedXML) and PDF (QuestPDF, RTL Arabic).
/// The "render every report" facts guard that every mapper produces a document both writers accept; the
/// Excel round-trip and the Arabic PDF snapshot (task 19) pin the concrete output. The snapshot PDF is
/// also written to the test output for the manual visual sign-off the task calls for.
/// </summary>
public sealed class ReportExportTests
{
    private static readonly ReportExcelRenderer Excel = new();
    private static readonly ReportPdfRenderer Pdf = new();
    private static readonly IReadOnlyDictionary<string, ReportDocument> Documents =
        SampleDocuments().ToDictionary(d => d.Name, d => d.Document);

    public static IEnumerable<object[]> AllReports() => Documents.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(AllReports))]
    public void EveryReport_RendersToValidXlsx(string name)
    {
        var bytes = Excel.Render(Documents[name]);

        IsXlsx(bytes).Should().BeTrue($"{name} should render a ZIP-based XLSX");

        // Re-open to prove it is a genuine, RTL workbook (not just well-formed bytes).
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.First();
        sheet.RightToLeft.Should().BeTrue($"{name} export must be right-to-left");
    }

    [Theory]
    [MemberData(nameof(AllReports))]
    public void EveryReport_RendersToValidPdf(string name)
    {
        var bytes = Pdf.Render(Documents[name]);

        IsPdf(bytes).Should().BeTrue($"{name} should render a valid PDF");
        bytes.Length.Should().BeGreaterThan(1024, $"{name} PDF should carry the embedded font + content");
    }

    [Fact]
    public void Excel_RoundTrips_TitleSummaryAndData()
    {
        var document = ReportDocuments.Sales(SampleSales());

        var bytes = Excel.Render(document);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.First();

        // Title is the first cell, Arabic.
        sheet.Cell(1, 1).GetString().Should().Be(ReportLabels.SalesTitle);

        // A money method row is present and stored as a real number (not a string), so Excel can total it.
        var cashCell = sheet.CellsUsed(c => c.GetString() == ReportLabels.PaymentMethod(PaymentMethod.Cash))
            .Single();
        var amountCell = sheet.Cell(cashCell.Address.RowNumber, cashCell.Address.ColumnNumber + 1);
        amountCell.DataType.Should().Be(XLDataType.Number);
        amountCell.GetDouble().Should().Be(120.50);
    }

    [Fact]
    public void Pdf_ArabicReport_IsValid_AndSnapshotWritten()
    {
        // Clinic-profits carries Arabic partner names + a table + summary — a representative Arabic page.
        var document = ReportDocuments.ClinicProfits(SampleClinicProfits());

        var bytes = Pdf.Render(document);

        IsPdf(bytes).Should().BeTrue();
        EndsWithEof(bytes).Should().BeTrue("a complete PDF ends with %%EOF");

        // Persist the artifact for the manual visual sign-off (task 19).
        var dir = Path.Combine(AppContext.BaseDirectory, "ReportSnapshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clinic-profits-ar.pdf");
        File.WriteAllBytes(path, bytes);
        File.Exists(path).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, ReportFormat.Json)]
    [InlineData("", ReportFormat.Json)]
    [InlineData("json", ReportFormat.Json)]
    [InlineData("JSON", ReportFormat.Json)]
    [InlineData("xlsx", ReportFormat.Xlsx)]
    [InlineData("excel", ReportFormat.Xlsx)]
    [InlineData(" XLS ", ReportFormat.Xlsx)]
    [InlineData("pdf", ReportFormat.Pdf)]
    public void Parse_NormalisesKnownFormats(string? input, ReportFormat expected) =>
        ReportFormats.Parse(input).Should().Be(expected);

    [Fact]
    public void Parse_RejectsUnknownFormat_WithTypedError()
    {
        var act = () => ReportFormats.Parse("csv");

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_format");
    }

    // ----- sample data ---------------------------------------------------------------------------

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 9, 30, 0, TimeSpan.Zero);

    private static IEnumerable<(string Name, ReportDocument Document)> SampleDocuments()
    {
        yield return ("doctor-income", ReportDocuments.DoctorIncome(new DoctorIncomeReportResponse(
            From, To, VisitType.Field, DoctorCount: 2, TotalVisitCount: 7, TotalRevenue: 1500m,
            TotalCalculatedShare: 450m,
            Rows:
            [
                new DoctorIncomeRow(Guid.NewGuid(), "د. سامر", 4, 900m, 270m),
                new DoctorIncomeRow(Guid.NewGuid(), "د. ليلى", 3, 600m, 180m),
            ])));

        yield return ("clinic-profits", ReportDocuments.ClinicProfits(SampleClinicProfits()));

        yield return ("profit-per-batch", ReportDocuments.ProfitPerBatch(new ProfitPerBatchReportResponse(
            BatchId: Guid.NewGuid(), CustomerId: Guid.NewGuid(), DoctorId: Guid.NewGuid(),
            EntitlementSystem: EntitlementSystem.DrugProfit, EntitlementEnabled: true,
            Revenue: 5000m, DrugCost: 2000m, DrugProfit: 3000m, ExamFee: 200m, DoctorShare: 840m,
            CeilingApplied: 840m, ClinicShare: 2160m, AsOf: To, DistributedToPartners: 2160m,
            RetainedByClinic: 0m,
            PartnerAllocations: [new ProfitAllocation(Guid.NewGuid(), "شريك أ", 100m, 2160m)])));

        yield return ("farm-account-status", ReportDocuments.FarmAccountStatus(new StatementResponse(
            CustomerId: Guid.NewGuid(), CustomerName: "مزرعة النور", FarmId: null, FarmName: null,
            LedgerId: Guid.NewGuid(),
            OpeningBalance: 0m, ClosingBalance: 350m, Status: LedgerStatus.HasDebt,
            From: Now, To: Now.AddMonths(1),
            Entries:
            [
                new LedgerEntryResponse(Guid.NewGuid(), Guid.NewGuid(), LedgerEntryType.Invoice, 500m, 500m,
                    Guid.NewGuid(), null, "فاتورة ميدانية", "idem-1", Now),
                new LedgerEntryResponse(Guid.NewGuid(), Guid.NewGuid(), LedgerEntryType.ReceiptVoucher, -150m, 350m,
                    null, Guid.NewGuid(), "سند قبض", "idem-2", Now.AddDays(3)),
            ])));

        yield return ("doctor-entitlements", ReportDocuments.DoctorEntitlements(
        [
            new DoctorEntitlementResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                EntitlementSystem.DrugProfit, 840m, 840m, EntitlementStatus.Approved, Guid.NewGuid(),
                Now, null, null, Now, Now),
            new DoctorEntitlementResponse(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
                EntitlementSystem.DirectFee, 200m, null, EntitlementStatus.Paid, Guid.NewGuid(),
                Now, Now.AddDays(2), PaymentMethod.Cash, Now, Now),
        ]));

        yield return ("sales", ReportDocuments.Sales(SampleSales()));

        yield return ("profit-and-loss", ReportDocuments.ProfitAndLoss(new ProfitAndLossResponse(
            From, To, Revenue: 8000m, TaxCollected: 480m, Cogs: 3000m, GrossProfit: 5000m, DoctorShares: 1200m)));

        yield return ("inventory-movement", ReportDocuments.InventoryMovement(new InventoryMovementReportResponse(
            From, To, ProductId: null, LocationType: null, LocationId: null, TotalCount: 1,
            Rows:
            [
                new InventoryMovementRow(StockLocation.Warehouse, Guid.NewGuid(), Guid.NewGuid(),
                    Inflows: 100m, Outflows: 40m, NetChange: 60m, Balance: 60m),
            ])));

        yield return ("field-doctor-visits", ReportDocuments.FieldDoctorVisits(new FieldDoctorVisitsReportResponse(
            From, To, DoctorId: Guid.NewGuid(), TotalCount: 1,
            Rows:
            [
                new FieldVisitRow(Guid.NewGuid(), "A-1001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    VisitStatus.Completed, Now, Now.AddHours(1),
                    Services: [new FieldVisitServiceLine(Guid.NewGuid(), "فحص ميداني", 40m)],
                    Medications:
                    [
                        new FieldVisitMedicationLine(Guid.NewGuid(), "مضاد حيوي", "5مل", 2m,
                            "administered_in_clinic"),
                    ]),
            ], NextCursor: null)));

        yield return ("kpi-summary", ReportDocuments.KpiSummary(new KpiSummaryResponse(
            AsOf: To, VisitsToday: 12, RevenueThisMonth: 9500m, PendingEntitlements: 3, LowStockItems: 5)));

        yield return ("upcoming-vaccinations", ReportDocuments.UpcomingVaccinations(
            new UpcomingVaccinationsReportResponse(From, To, CustomerId: null, TotalCount: 1,
                Rows:
                [
                    new UpcomingVaccinationRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                        "داء الكلب", new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 15)),
                ])));
    }

    private static SalesReportResponse SampleSales() => new(
        From, To, CashierId: null, Total: 320.50m, InvoiceCount: 4,
        ByMethod:
        [
            new SalesByMethod(PaymentMethod.Cash, 120.50m, 2),
            new SalesByMethod(PaymentMethod.Card, 200m, 2),
        ]);

    private static ClinicProfitsReportResponse SampleClinicProfits() => new(
        From, To, AsOf: To, Revenue: 10000m, Cogs: 4000m, NetProfit: 6000m, DoctorShares: 1500m,
        DistributedToPartners: 6000m, RetainedByClinic: 0m,
        PartnerAllocations:
        [
            new ProfitAllocation(Guid.NewGuid(), "شريك أول", 60m, 3600m),
            new ProfitAllocation(Guid.NewGuid(), "شريك ثانٍ", 40m, 2400m),
        ]);

    // ----- helpers -------------------------------------------------------------------------------

    private static bool IsXlsx(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    private static bool EndsWithEof(byte[] bytes)
    {
        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 8), Math.Min(8, bytes.Length));
        return tail.Contains("%%EOF", StringComparison.Ordinal);
    }
}
