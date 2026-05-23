using VetSystem.Domain.Common;

namespace VetSystem.Application.Entitlements;

/// <summary>Resolves the <see cref="IExamFeeCalculator"/> for a batch's <c>supervision_fee_model</c>
/// (PRD §7.3, M9 task 3). Throws a typed domain error for an unknown model.</summary>
public interface IExamFeeCalculatorFactory
{
    IExamFeeCalculator For(string feeModel);
}

/// <inheritdoc cref="IExamFeeCalculatorFactory"/>
public sealed class ExamFeeCalculatorFactory : IExamFeeCalculatorFactory
{
    private readonly IReadOnlyDictionary<string, IExamFeeCalculator> _calculators;

    public ExamFeeCalculatorFactory(IEnumerable<IExamFeeCalculator> calculators)
    {
        _calculators = calculators.ToDictionary(c => c.FeeModel, StringComparer.Ordinal);
    }

    public IExamFeeCalculator For(string feeModel)
        => _calculators.TryGetValue(feeModel, out var calculator)
            ? calculator
            : throw new ConflictException("invalid_fee_model", $"Unknown supervision fee model '{feeModel}'.");
}
