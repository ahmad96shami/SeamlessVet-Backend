using System.Globalization;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// The export-neutral shape of a report (M12 tasks 12–13). Every report service returns a typed JSON
/// DTO; for <c>?format=xlsx|pdf</c> the matching mapper in <see cref="ReportDocuments"/> projects that
/// DTO into this model, and a single Excel writer (<see cref="ReportExcelRenderer"/>) and a single PDF
/// writer render it. Keeping one model means the ClosedXML and QuestPDF code each live in exactly one
/// place and every report is Arabic-first / RTL by construction — the per-report code only describes
/// labels, columns and cells, never layout.
/// </summary>
public sealed record ReportDocument(
    string Title,
    string FileBaseName,
    IReadOnlyList<ReportField> Filters,
    IReadOnlyList<ReportField> Summary,
    IReadOnlyList<ReportTable> Tables);

/// <summary>A labelled scalar shown in the header block — an echoed request filter or a summary KPI.</summary>
public sealed record ReportField(string Label, string Value);

/// <summary>A tabular section: an optional Arabic caption, typed columns, and the rows to render.</summary>
public sealed record ReportTable(
    string? Caption,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<ReportCell>> Rows);

/// <summary>A column header plus the <see cref="ReportCellKind"/> its cells carry (drives alignment + number format).</summary>
public sealed record ReportColumn(string Header, ReportCellKind Kind);

/// <summary>
/// The semantic type of a <see cref="ReportCell"/>. Drives rendering (text aligns to the start, numbers
/// to the end) and lets the Excel writer emit a genuine numeric cell — so totals, filters and formatting
/// work in Excel — instead of a string.
/// </summary>
public enum ReportCellKind
{
    Text,
    Money,
    Number,
    Integer,
    Percent,
}

/// <summary>
/// One rendered cell. <see cref="Display"/> is the display string used by every renderer; <see cref="Numeric"/>
/// is set for the numeric kinds so the Excel writer can store a real number. Use the factory methods so
/// formatting (invariant culture, Western digits) stays consistent across all reports.
/// </summary>
public sealed record ReportCell(ReportCellKind Kind, string Display, decimal? Numeric)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ReportCell Text(string? value) => new(ReportCellKind.Text, value ?? string.Empty, null);

    public static ReportCell Money(decimal value) => new(ReportCellKind.Money, value.ToString("#,##0.00", Inv), value);

    public static ReportCell Number(decimal value) => new(ReportCellKind.Number, value.ToString("#,##0.###", Inv), value);

    public static ReportCell Integer(int value) => new(ReportCellKind.Integer, value.ToString("#,##0", Inv), value);

    public static ReportCell Percent(decimal value) => new(ReportCellKind.Percent, value.ToString("0.##", Inv) + "%", value);

    public static ReportCell Day(DateOnly? value) =>
        new(ReportCellKind.Text, value?.ToString("yyyy-MM-dd", Inv) ?? "—", null);

    public static ReportCell Moment(DateTimeOffset? value) =>
        new(ReportCellKind.Text, value?.ToString("yyyy-MM-dd HH:mm", Inv) ?? "—", null);

    public static ReportCell Id(Guid value) => new(ReportCellKind.Text, value.ToString(), null);

    public static ReportCell Id(Guid? value) => new(ReportCellKind.Text, value?.ToString() ?? "—", null);

    /// <summary>An optional money value, rendered as an em dash when absent (e.g. an unhit ceiling).</summary>
    public static ReportCell MoneyOrDash(decimal? value) =>
        value is { } v ? Money(v) : new ReportCell(ReportCellKind.Money, "—", null);
}
