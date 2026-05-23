using VetSystem.Domain.Common;

namespace VetSystem.API.Reports.Export;

/// <summary>
/// The render format requested via the <c>?format=</c> query parameter on every report endpoint
/// (M12 tasks 12–13). Absent / <c>json</c> keeps the typed JSON response; <c>xlsx</c> and <c>pdf</c>
/// stream a generated file. Normalisation is case-insensitive and rejects anything else up front so a
/// typo fails loudly instead of silently falling back to JSON.
/// </summary>
public enum ReportFormat
{
    Json,
    Xlsx,
    Pdf,
}

public static class ReportFormats
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";

    /// <summary>
    /// Maps the raw <c>?format=</c> value to a <see cref="ReportFormat"/>. Null/blank ⇒ <see cref="ReportFormat.Json"/>;
    /// <c>excel</c>/<c>xls</c> alias to <see cref="ReportFormat.Xlsx"/>.
    /// </summary>
    /// <exception cref="ConflictException">Code <c>invalid_format</c> for any unrecognised value.</exception>
    public static ReportFormat Parse(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return ReportFormat.Json;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "json" => ReportFormat.Json,
            "xlsx" or "excel" or "xls" => ReportFormat.Xlsx,
            "pdf" => ReportFormat.Pdf,
            _ => throw new ConflictException(
                "invalid_format", $"format '{format}' is not supported (use json, xlsx, or pdf)."),
        };
    }
}
