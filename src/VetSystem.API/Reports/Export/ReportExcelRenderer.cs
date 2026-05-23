using ClosedXML.Excel;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// Renders a <see cref="ReportDocument"/> to a single-sheet XLSX workbook (M12 task 12). The sheet is
/// right-to-left; the title, the echoed filters and the summary KPIs stack above the report's table(s).
/// Numeric kinds are written as real Excel numbers with a number format (not strings) so totals and
/// filtering work in Excel. This is the only place ClosedXML is used.
/// </summary>
public sealed class ReportExcelRenderer
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E5F");
    private static readonly XLColor HeaderText = XLColor.White;
    private static readonly XLColor CaptionFill = XLColor.FromHtml("#DCE6EB");

    public byte[] Render(ReportDocument document)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName(document.Title));
        sheet.RightToLeft = true;

        var width = Math.Max(2, document.Tables.Count == 0 ? 2 : document.Tables.Max(t => t.Columns.Count));
        var row = 1;

        // Title
        var titleCell = sheet.Cell(row, 1);
        titleCell.Value = document.Title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 16;
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(row, 1, row, width).Merge();
        row += 2;

        row = WriteFieldBlock(sheet, row, width, document.Filters);
        if (document.Filters.Count > 0 && document.Summary.Count > 0)
        {
            row++;
        }

        row = WriteFieldBlock(sheet, row, width, document.Summary);

        foreach (var table in document.Tables)
        {
            row++;
            row = WriteTable(sheet, row, width, table);
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Writes a label/value block (filters or summary) as two columns; returns the next free row.</summary>
    private static int WriteFieldBlock(IXLWorksheet sheet, int row, int width, IReadOnlyList<ReportField> fields)
    {
        foreach (var field in fields)
        {
            var label = sheet.Cell(row, 1);
            label.Value = field.Label;
            label.Style.Font.Bold = true;

            var value = sheet.Cell(row, 2);
            value.Value = field.Value;
            if (width > 2)
            {
                sheet.Range(row, 2, row, width).Merge();
            }

            row++;
        }

        return row;
    }

    /// <summary>Writes one table (optional caption, header row, data rows); returns the next free row.</summary>
    private static int WriteTable(IXLWorksheet sheet, int row, int width, ReportTable table)
    {
        if (table.Caption is { } caption)
        {
            var captionCell = sheet.Cell(row, 1);
            captionCell.Value = caption;
            captionCell.Style.Font.Bold = true;
            captionCell.Style.Fill.BackgroundColor = CaptionFill;
            sheet.Range(row, 1, row, width).Merge();
            row++;
        }

        var headerRow = row;
        for (var col = 0; col < table.Columns.Count; col++)
        {
            var cell = sheet.Cell(row, col + 1);
            cell.Value = table.Columns[col].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = HeaderText;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        row++;

        foreach (var dataRow in table.Rows)
        {
            for (var col = 0; col < table.Columns.Count; col++)
            {
                var cell = sheet.Cell(row, col + 1);
                WriteCell(cell, dataRow[col]);
            }

            row++;
        }

        // Border the whole table block (header + data), if there is any data.
        var lastRow = Math.Max(headerRow, row - 1);
        var range = sheet.Range(headerRow, 1, lastRow, table.Columns.Count);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        return row;
    }

    private static void WriteCell(IXLCell cell, ReportCell value)
    {
        if (value.Kind != ReportCellKind.Text && value.Numeric is { } numeric)
        {
            cell.Value = numeric;
            cell.Style.NumberFormat.Format = NumberFormat(value.Kind);
        }
        else
        {
            cell.Value = value.Display;
        }
    }

    private static string NumberFormat(ReportCellKind kind) => kind switch
    {
        ReportCellKind.Money => "#,##0.00",
        ReportCellKind.Number => "#,##0.###",
        ReportCellKind.Integer => "#,##0",
        ReportCellKind.Percent => "0.##\"%\"",
        _ => "General",
    };

    /// <summary>Excel sheet names cap at 31 chars and forbid <c>[ ] : * ? / \</c>; sanitise the title to fit.</summary>
    private static string SheetName(string title)
    {
        var cleaned = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Report";
        }

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
