using Codes = VetSystem.Domain.Entities;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// Arabic-first label catalog for the exported reports (M12 tasks 12–13). The report DTOs are
/// language-neutral codes (<c>cash</c>, <c>field</c>, <c>pending</c>, …); the export renders an
/// Arabic document, so every structural label (titles, filter/column headers) and every domain code
/// is translated here in one place. Translation is fail-safe: an unknown code renders as itself rather
/// than throwing, so a new enum value never breaks an export. Frontend i18n still lives in
/// <c>packages/shared</c>; this is the backend-only set the server-rendered files need.
/// </summary>
public static class ReportLabels
{
    // ----- Report titles -------------------------------------------------------------------------
    public const string DoctorIncomeTitle = "تقرير دخل الأطباء";
    public const string ClinicProfitsTitle = "تقرير أرباح العيادة";
    public const string ProfitPerBatchTitle = "تقرير ربحية دورة المزرعة";
    public const string FarmAccountStatusTitle = "كشف حساب العميل";
    public const string DoctorEntitlementsTitle = "مستحقات الأطباء";
    public const string SalesTitle = "تقرير المبيعات";
    public const string ProfitAndLossTitle = "قائمة الأرباح والخسائر";
    public const string InventoryMovementTitle = "تقرير حركة المخزون";
    public const string FieldDoctorVisitsTitle = "سجل الزيارات الميدانية";
    public const string KpiSummaryTitle = "ملخص المؤشرات";
    public const string UpcomingVaccinationsTitle = "التطعيمات القادمة";
    public const string PharmacyProfitTitle = "تقرير أرباح الصيدلية";
    public const string InClinicVisitProfitTitle = "تقرير أرباح زيارات العيادة";
    public const string FieldVisitProfitTitle = "تقرير أرباح الزيارات الميدانية";

    // ----- Shared field / column labels ----------------------------------------------------------
    public const string From = "من تاريخ";
    public const string To = "إلى تاريخ";
    public const string AsOf = "حتى تاريخ";
    public const string Doctor = "الطبيب";
    public const string Customer = "العميل";
    public const string Cashier = "الصرّاف";
    public const string Batch = "الدورة";
    public const string Product = "المنتج";
    public const string Location = "الموقع";
    public const string LocationKind = "نوع الموقع";
    public const string VisitTypeLabel = "نوع الزيارة";
    public const string Status = "الحالة";
    public const string System = "نظام الاحتساب";
    public const string Count = "العدد";
    public const string Farm = "المزرعة";
    public const string ScopeLabel = "النطاق";

    // Summary KPI labels
    public const string DoctorCount = "عدد الأطباء";
    public const string VisitCount = "عدد الزيارات";
    public const string TotalRevenue = "إجمالي الإيرادات";
    public const string TotalShare = "إجمالي المستحقات";
    public const string Revenue = "الإيرادات";
    public const string Cogs = "تكلفة البضاعة المباعة";
    public const string NetProfit = "صافي الربح";
    public const string GrossProfit = "إجمالي الربح";
    public const string DoctorShares = "حصص الأطباء";
    public const string DistributedToPartners = "الموزّع على الشركاء";
    public const string RetainedByClinic = "المحتجز للعيادة";
    public const string TaxCollected = "الضريبة المحصّلة";
    public const string DrugCost = "تكلفة الأدوية";
    public const string DrugProfit = "ربح الأدوية";
    public const string Cost = "التكلفة";
    public const string Profit = "الربح";
    public const string QuantitySold = "الكمية المباعة";
    public const string ExamFee = "رسوم الكشف";
    public const string DoctorShare = "حصة الطبيب";
    public const string ClinicShare = "حصة العيادة";
    public const string CeilingApplied = "السقف المطبَّق";
    public const string EntitlementEnabled = "المستحقات مفعّلة";
    public const string TotalCount = "إجمالي السجلات";
    public const string InvoiceCount = "عدد الفواتير";
    public const string Total = "الإجمالي";
    public const string OpeningBalance = "الرصيد الافتتاحي";
    public const string ClosingBalance = "الرصيد الختامي";
    public const string VisitsToday = "زيارات اليوم";
    public const string RevenueThisMonth = "إيرادات الشهر";
    public const string PendingEntitlements = "مستحقات قيد الانتظار";
    public const string LowStockItems = "أصناف منخفضة المخزون";

    // Table column headers
    public const string Partner = "الشريك";
    public const string SharePercent = "النسبة";
    public const string Amount = "المبلغ";
    public const string PaymentMethodHeader = "طريقة الدفع";
    public const string PaymentCount = "عدد الدفعات";
    public const string Date = "التاريخ";
    public const string EntryType = "النوع";
    public const string Description = "البيان";
    public const string Balance = "الرصيد";
    public const string Inflows = "الوارد";
    public const string Outflows = "الصادر";
    public const string NetChange = "صافي الحركة";
    public const string VisitNumber = "رقم الزيارة";
    public const string Services = "الخدمات";
    public const string Medications = "الأدوية";
    public const string StartedAt = "وقت البدء";
    public const string EndedAt = "وقت الانتهاء";
    public const string VaccineType = "نوع اللقاح";
    public const string DateGiven = "تاريخ الإعطاء";
    public const string NextDueDate = "تاريخ الاستحقاق";
    public const string ApprovedAt = "تاريخ الاعتماد";
    public const string PaidAt = "تاريخ الدفع";
    public const string PaidMethod = "طريقة الصرف";

    public const string Yes = "نعم";
    public const string No = "لا";

    public static string Bool(bool value) => value ? Yes : No;

    // ----- Domain-code translations --------------------------------------------------------------
    public static string PaymentMethod(string? code) => Lookup(PaymentMethods, code);
    public static string VisitType(string? code) => Lookup(VisitTypes, code);
    public static string VisitStatus(string? code) => Lookup(VisitStatuses, code);
    public static string InvoiceStatus(string? code) => Lookup(InvoiceStatuses, code);
    public static string EntitlementStatus(string? code) => Lookup(EntitlementStatuses, code);
    public static string EntitlementSystem(string? code) => Lookup(EntitlementSystems, code);
    public static string MovementType(string? code) => Lookup(MovementTypes, code);
    public static string LocationType(string? code) => Lookup(LocationTypes, code);
    public static string CustomerType(string? code) => Lookup(CustomerTypes, code);
    public static string LedgerEntryType(string? code) => Lookup(LedgerEntryTypes, code);
    public static string LedgerStatus(string? code) => Lookup(LedgerStatuses, code);
    public static string ScopeName(string? code) => Lookup(Scopes, code);

    private static string Lookup(IReadOnlyDictionary<string, string> map, string? code) =>
        code is not null && map.TryGetValue(code, out var label) ? label : code ?? "—";

    private static readonly IReadOnlyDictionary<string, string> PaymentMethods = new Dictionary<string, string>
    {
        [Codes.PaymentMethod.Cash] = "نقدي",
        [Codes.PaymentMethod.Card] = "بطاقة",
        [Codes.PaymentMethod.BankTransfer] = "حوالة بنكية",
        [Codes.PaymentMethod.Credit] = "آجل",
    };

    private static readonly IReadOnlyDictionary<string, string> VisitTypes = new Dictionary<string, string>
    {
        [Codes.VisitType.InClinic] = "داخل العيادة",
        [Codes.VisitType.Field] = "ميداني",
    };

    private static readonly IReadOnlyDictionary<string, string> VisitStatuses = new Dictionary<string, string>
    {
        [Codes.VisitStatus.Open] = "مفتوحة",
        [Codes.VisitStatus.InProgress] = "قيد التنفيذ",
        [Codes.VisitStatus.Completed] = "مكتملة",
        [Codes.VisitStatus.Cancelled] = "ملغاة",
    };

    private static readonly IReadOnlyDictionary<string, string> InvoiceStatuses = new Dictionary<string, string>
    {
        [Codes.InvoiceStatus.Issued] = "صادرة",
        [Codes.InvoiceStatus.Flagged] = "معلّمة",
        [Codes.InvoiceStatus.Void] = "ملغاة",
    };

    private static readonly IReadOnlyDictionary<string, string> EntitlementStatuses = new Dictionary<string, string>
    {
        [Codes.EntitlementStatus.Pending] = "قيد الانتظار",
        [Codes.EntitlementStatus.Approved] = "معتمدة",
        [Codes.EntitlementStatus.Paid] = "مدفوعة",
    };

    private static readonly IReadOnlyDictionary<string, string> EntitlementSystems = new Dictionary<string, string>
    {
        [Codes.EntitlementSystem.DrugProfit] = "ربح الأدوية",
        [Codes.EntitlementSystem.DirectFee] = "أتعاب مباشرة",
    };

    private static readonly IReadOnlyDictionary<string, string> MovementTypes = new Dictionary<string, string>
    {
        [Codes.MovementType.Receive] = "استلام",
        [Codes.MovementType.Adjust] = "تسوية",
        [Codes.MovementType.LoadToField] = "تحميل ميداني",
        [Codes.MovementType.UnloadFromField] = "إرجاع من الميدان",
        [Codes.MovementType.SaleDeduct] = "خصم بيع",
        [Codes.MovementType.ReturnAdd] = "إضافة مرتجع",
    };

    private static readonly IReadOnlyDictionary<string, string> LocationTypes = new Dictionary<string, string>
    {
        [Codes.StockLocation.Warehouse] = "المستودع",
        [Codes.StockLocation.Field] = "ميداني",
    };

    private static readonly IReadOnlyDictionary<string, string> CustomerTypes = new Dictionary<string, string>
    {
        [Codes.CustomerType.RegularFarm] = "مزرعة عادية",
        [Codes.CustomerType.Home] = "منزل",
        [Codes.CustomerType.CattleFarm] = "مزرعة أبقار",
        [Codes.CustomerType.PoultryFarm] = "مزرعة دواجن",
    };

    private static readonly IReadOnlyDictionary<string, string> LedgerEntryTypes = new Dictionary<string, string>
    {
        [Codes.LedgerEntryType.Invoice] = "فاتورة",
        [Codes.LedgerEntryType.ServiceFee] = "رسوم خدمة",
        [Codes.LedgerEntryType.ExamFee] = "رسوم كشف",
        [Codes.LedgerEntryType.ReceiptVoucher] = "سند قبض",
        [Codes.LedgerEntryType.Adjustment] = "تسوية",
    };

    private static readonly IReadOnlyDictionary<string, string> LedgerStatuses = new Dictionary<string, string>
    {
        [Codes.LedgerStatus.Open] = "مفتوح",
        [Codes.LedgerStatus.HasDebt] = "عليه دين",
        [Codes.LedgerStatus.Closed] = "مغلق",
    };

    // The farm/clinic slicer on the visit-profit reports (M20). Codes are not domain enums — they are
    // the report's own query values, so they live here rather than in Domain.
    private static readonly IReadOnlyDictionary<string, string> Scopes = new Dictionary<string, string>
    {
        ["farm"] = "مزرعة",
        ["clinic"] = "عيادة",
    };
}
