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

/// <summary>
/// Well-known <see cref="Service.Category"/> values the server itself depends on. Vaccination is
/// the M22 vaccine-catalog convention (admin-managed rows); checkup / night-stay are the M23
/// <b>system services</b> — find-or-created per environment at issuance so checkup-fee and
/// night-stay invoice lines satisfy the product-XOR-service CHECK on <c>invoice_items</c>.
/// At most one service per environment may carry a system category (partial unique index).
/// </summary>
public static class ServiceCategories
{
    public const string Vaccination = "vaccination";
    public const string Checkup = "checkup";
    public const string NightStay = "night_stay";
}
