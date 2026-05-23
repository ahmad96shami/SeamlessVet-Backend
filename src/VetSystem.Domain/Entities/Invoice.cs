using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §8 — a POS sale, a field-visit invoice, or a standalone exam-fee invoice. Append-only
/// (SCHEMA "Key invariants" #3): an issued invoice is never UPDATEd or DELETEd. Corrections are a
/// new row with <see cref="Status"/> = <c>void</c> pointing back via <see cref="VoidOfInvoiceId"/>,
/// plus a compensating <c>adjustment</c> ledger entry — the original row stays untouched.
/// <see cref="CustomerId"/> is null for a walk-in sale (PRD §5.4); those skip ledger posting.
/// </summary>
public sealed class Invoice : Entity
{
    public string InvoiceType { get; set; } = Entities.InvoiceType.Pos;

    /// <summary>Null for a walk-in sale (no owner, no ledger posting).</summary>
    public Guid? CustomerId { get; set; }

    public Guid? VisitId { get; set; }

    /// <summary>Links the invoice into a batch's drug-profit calc (M8/M9). FK target lands in M8.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Human-friendly per-user-prefixed number (SCHEMA "Key invariants" #9). Null on void rows.</summary>
    public string? Number { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    public string Status { get; set; } = InvoiceStatus.Issued;

    public Guid IssuedBy { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Set on a <c>void</c> row to the id of the invoice it reverses (additive M7 column; the SCHEMA
    /// reference predates the append-a-row void model). Null on normal invoices.
    /// </summary>
    public Guid? VoidOfInvoiceId { get; set; }
}

public static class InvoiceType
{
    public const string Pos = "pos";
    public const string Field = "field";
    public const string ExamFee = "exam_fee";

    public static readonly IReadOnlyCollection<string> All = [Pos, Field, ExamFee];
}

public static class InvoiceStatus
{
    public const string Issued = "issued";
    public const string Flagged = "flagged";
    public const string Void = "void";

    public static readonly IReadOnlyCollection<string> All = [Issued, Flagged, Void];
}
