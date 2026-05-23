using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// Renders a <see cref="ReportDocument"/> to a right-to-left, Arabic PDF (M12 task 13). The page content
/// flows right-to-left and uses the embedded <b>Noto Sans Arabic</b> font (bundled in this assembly), so
/// Arabic shapes correctly in any environment — CI, Docker — without depending on system fonts. This is
/// the only place QuestPDF is used. The QuestPDF Community licence + font registration run once in the
/// static constructor.
/// </summary>
public sealed class ReportPdfRenderer
{
    private const string FontFamily = "Noto Sans Arabic";
    private static readonly Color HeaderBg = Colors.Teal.Darken3;
    private static readonly Color CaptionBg = Colors.Grey.Lighten3;
    private static readonly Color RuleColor = Colors.Grey.Lighten1;

    static ReportPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        RegisterFont("NotoSansArabic-Regular.ttf");
        RegisterFont("NotoSansArabic-Bold.ttf");
    }

    public byte[] Render(ReportDocument document) =>
        Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(9).DirectionFromRightToLeft());
                    page.ContentFromRightToLeft();

                    page.Header().Element(header => ComposeHeader(header, document));
                    page.Content().PaddingVertical(8).Element(content => ComposeBody(content, document));
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            })
            .GeneratePdf();

    private static void ComposeHeader(IContainer container, ReportDocument document)
    {
        container.Column(column =>
        {
            column.Item().Text(document.Title).FontSize(16).Bold();
            column.Item().PaddingTop(4).LineHorizontal(1).LineColor(HeaderBg);
        });
    }

    private static void ComposeBody(IContainer container, ReportDocument document)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            if (document.Filters.Count > 0)
            {
                column.Item().Element(c => ComposeFields(c, document.Filters));
            }

            if (document.Summary.Count > 0)
            {
                column.Item().Element(c => ComposeFields(c, document.Summary));
            }

            foreach (var table in document.Tables)
            {
                column.Item().Element(c => ComposeTable(c, table));
            }
        });
    }

    /// <summary>Renders a label/value block (filters or summary) as one "label: value" line per field.</summary>
    private static void ComposeFields(IContainer container, IReadOnlyList<ReportField> fields)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            foreach (var field in fields)
            {
                column.Item().Text(text =>
                {
                    text.Span($"{field.Label}: ").SemiBold();
                    text.Span(field.Value);
                });
            }
        });
    }

    private static void ComposeTable(IContainer container, ReportTable table)
    {
        container.Column(column =>
        {
            if (table.Caption is { } caption)
            {
                column.Item().Background(CaptionBg).Padding(4).Text(caption).SemiBold();
            }

            if (table.Rows.Count == 0)
            {
                column.Item().PaddingVertical(4).Text("لا توجد بيانات").FontColor(Colors.Grey.Darken1);
                return;
            }

            column.Item().Table(t =>
            {
                t.ColumnsDefinition(columns =>
                {
                    foreach (var _ in table.Columns)
                    {
                        columns.RelativeColumn();
                    }
                });

                t.Header(header =>
                {
                    foreach (var col in table.Columns)
                    {
                        header.Cell()
                            .Background(HeaderBg)
                            .Padding(4)
                            .AlignCenter()
                            .Text(col.Header)
                            .FontColor(Colors.White)
                            .SemiBold();
                    }
                });

                foreach (var row in table.Rows)
                {
                    for (var col = 0; col < table.Columns.Count; col++)
                    {
                        var cell = row[col];
                        Align(table.Columns[col].Kind, t.Cell().BorderBottom(0.5f).BorderColor(RuleColor).Padding(3))
                            .Text(cell.Display);
                    }
                }
            });
        });
    }

    /// <summary>Numeric columns hug the end (left in RTL); text columns hug the start (right).</summary>
    private static IContainer Align(ReportCellKind kind, IContainer cell) =>
        kind == ReportCellKind.Text ? cell.AlignRight() : cell.AlignLeft();

    private static void RegisterFont(string fileName)
    {
        var assembly = typeof(ReportPdfRenderer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded font '{fileName}' was not found in {assembly.GetName().Name}. " +
                "Ensure it is included as an <EmbeddedResource> in VetSystem.API.csproj.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded font stream '{resourceName}'.");
        FontManager.RegisterFont(stream);
    }
}
