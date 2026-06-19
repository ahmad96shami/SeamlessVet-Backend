using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// A general operating expense the center incurs (water, electricity, rent, …) that is not tied to an
/// inventory purchase or payroll. Incurred expenses reduce the center's net profit for the period
/// (revenue − COGS − operating expenses); unpaid ones count toward "amount owed to others" in the
/// clinic-profit report. Center-web only (admin/accountant); not part of any field-doctor sync scope.
/// </summary>
public sealed class OperatingExpense : Entity
{
    public string Category { get; set; } = OperatingExpenseCategory.Other;

    /// <summary>Expense amount (ex-anything); always positive.</summary>
    public decimal Amount { get; set; }

    /// <summary>The date the expense was incurred (drives which reporting period it lands in).</summary>
    public DateOnly IncurredOn { get; set; }

    public bool Paid { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public string? Note { get; set; }

    /// <summary>The user who recorded the expense.</summary>
    public Guid RecordedBy { get; set; }
}

/// <summary>The fixed set of operating-expense categories surfaced in the UI.</summary>
public static class OperatingExpenseCategory
{
    public const string Water = "water";
    public const string Electricity = "electricity";
    public const string Rent = "rent";
    public const string Internet = "internet";
    public const string Maintenance = "maintenance";
    public const string Other = "other";

    public static readonly IReadOnlyCollection<string> All =
    [
        Water, Electricity, Rent, Internet, Maintenance, Other,
    ];
}
