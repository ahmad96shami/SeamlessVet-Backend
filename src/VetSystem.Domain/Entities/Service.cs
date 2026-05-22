using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §3 — billable service (clinic procedure, field service, vaccination as a line, exam
/// fee as a line). Pull-only on clients via the PowerSync <c>reference</c> bucket; writes are
/// admin-only through <c>/admin/services</c>.
/// </summary>
public sealed class Service : Entity
{
    public string NameAr { get; set; } = string.Empty;

    public string? NameLatin { get; set; }

    public string? Category { get; set; }

    public decimal DefaultPrice { get; set; }
}
