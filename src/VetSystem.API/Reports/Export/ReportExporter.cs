using VetSystem.Domain.Common;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// The single decision point for the report endpoints' <c>?format=</c> parameter (M12 tasks 12–13).
/// Each endpoint computes its typed report, then hands it here together with the mapper that turns it
/// into a <see cref="ReportDocument"/>. JSON is returned as-is; <c>xlsx</c>/<c>pdf</c> stream a generated
/// file with a header-safe download name. The mapper runs lazily — only when a file is actually rendered,
/// so a plain JSON request pays nothing for the export model.
/// </summary>
public sealed class ReportExporter
{
    private readonly ReportExcelRenderer _excel;
    private readonly ReportPdfRenderer _pdf;

    public ReportExporter(ReportExcelRenderer excel, ReportPdfRenderer pdf)
    {
        _excel = excel;
        _pdf = pdf;
    }

    public IResult Resolve<T>(string? format, T jsonPayload, Func<T, ReportDocument> toDocument)
    {
        switch (ReportFormats.Parse(format))
        {
            case ReportFormat.Xlsx:
            {
                var document = toDocument(jsonPayload);
                return TypedResults.File(
                    _excel.Render(document), ReportFormats.XlsxContentType, $"{document.FileBaseName}.xlsx");
            }

            case ReportFormat.Pdf:
            {
                var document = toDocument(jsonPayload);
                return TypedResults.File(
                    _pdf.Render(document), ReportFormats.PdfContentType, $"{document.FileBaseName}.pdf");
            }

            case ReportFormat.Json:
                return TypedResults.Ok(jsonPayload);

            default:
                // Unreachable: ReportFormats.Parse throws on anything else. Keeps the switch exhaustive.
                throw new ConflictException("invalid_format", $"format '{format}' is not supported.");
        }
    }
}
