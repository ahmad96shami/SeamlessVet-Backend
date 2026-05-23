using DomainFeeModel = VetSystem.Domain.Entities.FeeModel;

namespace VetSystem.Application.Entitlements;

/// <summary>The four exam-fee models (PRD §7.3). All round to 2dp (money). Each is registered as an
/// <see cref="IExamFeeCalculator"/> and dispatched by <see cref="IExamFeeCalculatorFactory"/>.</summary>
internal static class ExamFeeMath
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Flat fee per visit or per batch (PRD §7.3 "Fixed amount"). The fee is the value itself.</summary>
public sealed class FixedAmountExamFeeCalculator : IExamFeeCalculator
{
    public string FeeModel => DomainFeeModel.FixedAmount;

    public decimal Calculate(ExamFeeBasis basis) => ExamFeeMath.Round(basis.FeeValue);
}

/// <summary>A percentage of the invoice value (PRD §7.3 "Percentage of invoice"). 100% → the whole
/// invoice; a zero invoice → 0.</summary>
public sealed class PercentOfInvoiceExamFeeCalculator : IExamFeeCalculator
{
    public string FeeModel => DomainFeeModel.PercentOfInvoice;

    public decimal Calculate(ExamFeeBasis basis) => ExamFeeMath.Round(basis.FeeValue / 100m * basis.InvoiceTotal);
}

/// <summary>Per-bird rate × animal count (PRD §7.3 "Per-bird", poultry supervision). Zero birds → 0.</summary>
public sealed class PerBirdExamFeeCalculator : IExamFeeCalculator
{
    public string FeeModel => DomainFeeModel.PerBird;

    public decimal Calculate(ExamFeeBasis basis) => ExamFeeMath.Round(basis.FeeValue * basis.AnimalCount);
}

/// <summary>Flat fee for the whole supervision cycle (PRD §7.3 "Per-batch fixed"). At batch
/// granularity this coincides numerically with <see cref="FixedAmountExamFeeCalculator"/>; the
/// distinction is semantic (per-cycle vs per-visit), kept as separate models for reporting clarity.</summary>
public sealed class PerBatchFixedExamFeeCalculator : IExamFeeCalculator
{
    public string FeeModel => DomainFeeModel.PerBatchFixed;

    public decimal Calculate(ExamFeeBasis basis) => ExamFeeMath.Round(basis.FeeValue);
}
