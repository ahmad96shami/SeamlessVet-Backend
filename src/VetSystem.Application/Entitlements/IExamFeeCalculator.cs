namespace VetSystem.Application.Entitlements;

/// <summary>
/// One examination/supervision-fee model (PRD §7.3). Each implementation owns a single
/// <see cref="VetSystem.Domain.Entities.FeeModel"/> value and is selected by
/// <see cref="IExamFeeCalculatorFactory"/>. Pure (no DB) so the four models unit-test in isolation.
///
/// <para>The resulting fee is used two ways (PRD §7.4): in <b>System A</b> (drug profit) it is
/// subtracted from profit before the doctor's percentage; in <b>System B</b> (direct fee) it <i>is</i>
/// the doctor's entitlement.</para>
/// </summary>
public interface IExamFeeCalculator
{
    /// <summary>The <see cref="VetSystem.Domain.Entities.FeeModel"/> this calculator handles.</summary>
    string FeeModel { get; }

    decimal Calculate(ExamFeeBasis basis);
}

/// <summary>
/// Inputs a fee model draws from. <see cref="FeeValue"/> is the batch's <c>supervision_fee_value</c>
/// (a flat amount, a per-bird rate, or a percent, depending on the model). <see cref="AnimalCount"/>
/// drives <c>per_bird</c>; <see cref="InvoiceTotal"/> drives <c>percent_of_invoice</c>.
/// </summary>
public sealed record ExamFeeBasis(decimal FeeValue, int AnimalCount, decimal InvoiceTotal);
